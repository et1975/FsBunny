namespace FsBunny

open System
open RabbitMQ.Client
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

/// Implementation
module Rmq = 
    open RabbitMQ.Client.Events
    open System.Collections.Generic

    let buildConsumer autoAck assember withBinding = 
        let consumerRef = ref Option<QueueingBasicConsumer>.None
        let withConsumer cont = 
            fun () ->
                match !consumerRef with
                | Some consumer when consumer.Model.IsOpen && consumer.IsRunning -> consumer
                | _ -> fun (name, channel) ->
                            let consumer = QueueingBasicConsumer(channel)
                            consumerRef := Some consumer
                            channel.BasicConsume(name, autoAck, consumer) |> ignore
                            while not <| consumer.IsRunning do System.Threading.Thread.Sleep 1
                            consumer
                       |> withBinding
            |> lock consumerRef
            |> cont

        withConsumer ignore // force the subscription to occur

        { new Consumer<'T> with
            member x.Get timeout =
                fun (consumer:QueueingBasicConsumer) ->
                    match consumer.Queue.Dequeue (int timeout*1000) with
                    | true, evt -> 
                        try 
                            Some { msg = assember(evt.RoutingKey,evt.BasicProperties.Headers,evt.Body)
                                   id = evt.DeliveryTag }
                        with ex -> // dead letter policy should be used to handle permanently rejected/undeliverable: http://www.rabbitmq.com/dlx.html
                            consumer.Model.BasicNack(evt.DeliveryTag, false, false)
                            None
                    | _ -> None
                |> withConsumer
            member x.Ack id = withConsumer <| fun consumer -> consumer.Model.BasicAck(id, false)
            member x.Nack id = withConsumer <| fun consumer -> consumer.Model.BasicNack(id, false, true)
        }
    
    let buildPublisher retries disassembler withChannel = 
        let rec attemptSend n (exchange,headers,smsg) = 
            try 
                withChannel
                <| fun (channel:IModel) ->
                        let props = channel.CreateBasicProperties()
                        props.DeliveryMode <- 2uy // persistent mode
                        match headers with
                        | Some h -> props.Headers <- h
                        | _ -> ()
                        match exchange with
                        | Direct name -> channel.BasicPublish(name, null, props, smsg)
                        | Routed(name, topic) -> channel.BasicPublish(name, topic, props, smsg)
            with ex -> 
                if n > 0us then attemptSend (n - 1us) (exchange,headers,smsg)
                else reraise()
        disassembler >> attemptSend retries


/// RabbitMqEventStreams is a factory to construct event-stream publishers and consumers.
/// factory: RabbitMQ connection factory to use.
/// defaultExchange: default exchange to bind to.
/// retries: number of reconnect attempts.
/// limit: prefetch limit.
type RabbitMqEventStreams(factory : ConnectionFactory, defaultExchange : string, retries : uint16, limit:uint16) = 
    let withConnection = 
        let connectionRef = ref (Option<IConnection>.None)
        fun cont ->
            fun () -> 
                match !connectionRef with
                | Some c when c.IsOpen -> c
                | _ -> connectionRef := Some(factory.CreateConnection())
                       (!connectionRef).Value
            |> lock connectionRef
            |> cont

    let openChannel() =
        let channelRef = ref (Option<IModel>.None)
        (fun cont ->
            match !channelRef with
            | Some channel when channel.IsOpen -> channel
            | _ -> channelRef := Some (withConnection <| fun conn -> conn.CreateModel())
                   (!channelRef).Value
            |> cont
        ,fun () -> (!channelRef).Value.Dispose())

    let withBinding exchange queue = 
        let bindingRef = ref Option<string*IModel>.None
        fun cont -> 
            match (!bindingRef) with
            | Some (name,channel) when channel.IsOpen -> (name,channel)
            | _ -> 
                let channel = withConnection <| fun conn -> conn.CreateModel()
                channel.BasicQos(0u,limit,false)
                let q = 
                    match queue with
                    | Persistent name -> channel.QueueDeclare(name, true, false, false, Map.empty)
                    | Temporary -> channel.QueueDeclare(Guid.NewGuid().ToString(), false, true, true, Map.empty)
                match exchange with
                | Direct name -> channel.QueueBind(q.QueueName, name, null)
                | Routed(name, topic) -> channel.QueueBind(q.QueueName, name, topic)
                bindingRef := Some(q.QueueName, channel)
                (!bindingRef).Value
            |> cont
    
    new(factory : ConnectionFactory, defaultExchange : string) = RabbitMqEventStreams(factory, defaultExchange, 3us, 300us)

    interface EventStreams with
        member x.Default() = Direct defaultExchange
        member x.Routed topic = Routed(defaultExchange, topic)
        member x.GetPublisher<'T> disassembler : Publisher<'T> = 
            openChannel()
            |> fst
            |> Rmq.buildPublisher retries disassembler

        member x.GetConsumer<'T> queue exchange assember: Consumer<'T> = 
            withBinding exchange queue
            |> Rmq.buildConsumer queue.AutoAck assember

        member x.UsingPublisher<'T> disassembler (cont:Publisher<'T> -> unit) = 
            let (withChannel,dispose) = openChannel()
            withChannel
            |> Rmq.buildPublisher retries disassembler
            |> cont 
            dispose()

