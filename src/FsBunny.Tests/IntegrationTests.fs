module IntegrationTests

open System
open NUnit.Framework
open Swensen.Unquote
open FSharp.Data.UnitSystems.SI.UnitSymbols
open FsBunny
open FsBunny.Assemblers
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open Google.Protobuf.WellKnownTypes

let streams =
    RabbitMqEventStreams(RabbitMQ.Client.ConnectionFactory(),"amq.topic",3us,2000us) :> EventStreams

[<Test>]
let ``Can instantiate the streams``() = 
    test <@ not <| Object.ReferenceEquals(null, streams) @>

[<Test>]
let ``MQTT broker status parses``() = 
    let status = MqttAssembly.sysBrokerStatusAssembler "$SYS.broker.connection.*.state" ("$SYS.broker.connection.bridge:01.state",(),[|1uy|]) 
    status =! ("bridge:01",1uy)

[<Test>]
[<Category("interactive")>]
let ``Consumer stream performs bind``() = 
    let consumer = streams.GetProtoConsumer<Int32Value> (Persistent "test.bind") (Routed("amq.topic", "test.bind")) 
    test <@ not <| Object.ReferenceEquals(null, consumer) @>

[<Test>]
[<Category("interactive")>]
let ``RabbitMQ raw event roundtrips``() = 
    let consumer = streams.GetConsumer<int> Temporary (Routed("amq.topic", "test.*.roundtrip")) (fun (topic,headers,_) -> Int32.Parse(topic.Split('.').[1]))
    let publish = streams.GetPublisher<int> (fun x -> Routed("amq.topic", sprintf "test.%d.roundtrip" x),None,[||])
    
    publish 1

    match consumer.Get 10<s> with
    | Some r -> r.msg =! 1
    | _ -> failwith "should have gotten the message we just sent"
    
[<Test>]
[<Category("interactive")>]
let ``RabbitMQ event roundtrips``() = 
    let consumer = streams.GetProtoConsumer<Int32Value> Temporary (Routed("amq.topic", "test.roundtrip"))
    let publish = streams.GetProtoPublisher<Int32Value> (Routed("amq.topic", "test.roundtrip"))
    
    publish (Int32Value(Value = 1))

    match consumer.Get 10<s> with
    | Some r -> r.msg.Value =! 1
    | _ -> failwith "should have gotten the message we just sent"

[<Test>]
[<Category("interactive")>]
let ``Consumer can read and ack concurrently``() = 
    let consumer = streams.GetProtoConsumer<Int32Value> (Persistent "test_conc") (Routed("amq.topic", "test.conc"))
    let publish = streams.GetProtoPublisher<Int32Value> (Routed("amq.topic", "test.conc"))
    let ms = System.Collections.Concurrent.ConcurrentStack()
    let total = 10000

    async {
        do! async { 
            for i in [1..total] do
                publish (Int32Value(Value = i))
        }

        async { 
            let mutable count = 0
            while count < total do
                consumer.Get(1<s>) 
                |> function 
                | Some m -> ms.Push m; count <- count + 1
                | None -> ()
        } |> Async.Start
        
        Threading.Thread.Sleep (TimeSpan.FromSeconds 2.0)
        do! async { 
            let mutable count = 0
            while count < total do
                ms.TryPop() 
                |> function 
                | true,msg -> consumer.Ack msg.id; count <- count + 1
                | _ -> () 
        }
    } |> Async.RunSynchronously


[<Test>]
[<Category("interactive")>]
let ``Consumer reconnects``() = 
    let rmq = Process.Start("rabbitmq-server")
    Thread.Sleep (TimeSpan.FromSeconds 10.0)

    let consumer = streams.GetProtoConsumer<Int32Value> (Persistent "test_conc") (Routed("amq.topic", "test.conc"))
    let publish = streams.GetProtoPublisher<Int32Value> (Routed("amq.topic", "test.conc"))

    publish (Int32Value(Value = 0))

    rmq.CloseMainWindow() |> ignore
    rmq.WaitForExit()

    Thread.Sleep (TimeSpan.FromSeconds 5.0)
    raises<RabbitMQ.Client.Exceptions.BrokerUnreachableException> <@ fun () -> consumer.Get(1<s>) @>

    let rmq = Process.Start("rabbitmq-server")
    Thread.Sleep (TimeSpan.FromSeconds 10.0)
    consumer.Get(1<s>) |> fun r -> consumer.Ack r.Value.id

    rmq.CloseMainWindow() |> ignore
    rmq.WaitForExit()


[<Test>]
[<Category("interactive")>]
[<Category("load")>]
let ``Messages proto throuput``() = 
    let consumer = streams.GetProtoConsumer<ListValue> Temporary (Routed("amq.topic", "test.throuput"))
    let publish = streams.GetProtoPublisher<ListValue> (Routed("amq.topic", "test.throuput"))
    
    let total = 10000
    let size = 100
    async {
        for i in [1..total] do
            let vs = ListValue()
            vs.Values.Add [|for v in 1..size -> Value(NumberValue = (float v)) |]
            vs |> publish 
    } |> Async.Start

    let sw = System.Diagnostics.Stopwatch.StartNew()
    consumer 
        |> Consumer.toSeq (fun get -> get 1<s>)
        |> Seq.take total
        |> Seq.length =! total
    
    Console.WriteLine( sprintf "Proto Throuput: %A (%AB each) in %A (%Am/s)" total (size*sizeof<float>) sw.Elapsed (float(total)/sw.Elapsed.TotalSeconds))


[<Test>]
[<Category("interactive")>]
[<Category("load")>]
let ``Messages pickler throuput``() = 
    let consumer = streams.GetPicklerConsumer<float list> Temporary (Routed("amq.topic", "test.throuput"))
    let publish = streams.GetPicklerPublisher<float list> (Routed("amq.topic", "test.throuput"))
    
    let total = 10000
    let size = 100
    async {
        for i in [1..total] do
            [for v in 1..size -> (float v)]
            |> publish 
    } |> Async.Start

    let sw = System.Diagnostics.Stopwatch.StartNew()
    consumer 
        |> Consumer.toSeq (fun get -> get 1<s>)
        |> Seq.take total
        |> Seq.length =! total
    
    Console.WriteLine( sprintf "Pickler Throuput: %A (%AB each) in %A (%Am/s)" total (size*sizeof<float>) sw.Elapsed (float(total)/sw.Elapsed.TotalSeconds))

