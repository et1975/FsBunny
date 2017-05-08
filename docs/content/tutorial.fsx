(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../build_output"
#I "../../packages/build/Newtonsoft.Json"
#I "../../packages/build/Fable.Json"

open System.Collections.Generic

(**
Namespaces
========================

FsBunny is organized into 3 APIs:

- EventStreams API - connects to RabbitMQ and provides constructors for publisher and consumers 
- Assembers - modules for packing and unpacking messages in and out of RabbitMQ primitives
- Publisher and Consumer types - the primary means of interaction


EventStreams API
========================
RabbitMqEventStreams implements the API, you'll need only one instance per process.
It takes several arguments:
*)

#r "FsBunny.dll"
#r "RabbitMQ.Client.dll"
open FsBunny

let streams = 
    RabbitMqEventStreams(
            RabbitMQ.Client.ConnectionFactory(), // underlying connection factory
            "amq.topic",                         // default exchange
            3us,                                 // number of reconnect retries (publisher only)
            2000us) :> EventStreams              // prefetch limit

(**
The API is thread-safe, every consumer and publisher gets a dedicated channel.
The connection is obtained when required and in case of Publisher will attempt to reconnect specified number of time times.
The consumer will ensure a connection on every `Get` request.


Assemblers and Messages
========================
Assemblers grew out of serializers by necessity to inspect/provide metadata in addition to message body.
Messages can be anything, and it's up to assembler to figure out how to map it to/from RMQ primitives. 
For example, MQTT status message has no payload and the topic itself carries the ONLINE/OFFLINE indication.

You may want to provide your own and the implementation is as simple as two functions, here's an example of a Fable-compatible JSON assembler:

*)
#r "Newtonsoft.Json.dll"
#r "Fable.JsonConverter.dll"

module JsonAssembly =
    open FsBunny
    open System
    open System.IO
    open Newtonsoft.Json

    let serializer = 
        let s = JsonSerializer.CreateDefault()
        s.Converters.Add(Fable.JsonConverter())
        s.Converters.Add(Converters.IsoDateTimeConverter (DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fff'Z'"))
        s

    // disassemble a message into RMQ primitives: target exchange, properties and payload
    let disassembler exchange (item:'T) : (Exchange * IDictionary<string, obj> option * byte []) = 
        use ms = new MemoryStream()
        use sw = new StreamWriter (ms)
        use jw = new JsonTextWriter (sw)
        serializer.Serialize (jw,item)
        jw.Flush()
        exchange, None, ms.ToArray()

    // assemble a message from RMQ primitives
    let assembler (topic:string, properties:IDictionary<string, obj>, payload:byte[]) : 'T = 
        use ms = new MemoryStream(payload)
        use sr = new StreamReader (ms)
        use jr = new JsonTextReader (sr)
        serializer.Deserialize<'T>(jr)

    // and let's expose the functionality as extensions on EventStreams
    type FsBunny.EventStreams with 
       /// Construct a consumer, using specified message type, queue, the exchange to bind to and Proto assember.
        member this.GetJsonConsumer<'T> (queue: Queue) (exchange:Exchange) : Consumer<'T> =
            this.GetConsumer<'T> queue exchange assembler

        /// Construct a publisher for the specified message type using Json disassembler.
        member this.GetJsonPublisher<'T> (exchange: Exchange) : Publisher<'T> = 
            this.GetPublisher<'T> (disassembler exchange)

        /// Use a publisher with a continuation for the specified message type using Json disassembler.
        member this.UsingJsonPublisher<'T> (exchange: Exchange) (cont:Publisher<'T> -> unit) : unit =
            this.UsingPublisher<'T> (disassembler exchange) cont


(**
In case of a failure to assemble a message from RMQ primitives, the message will be Nacked and go into a Dead Letter queue (if one is setup).

Publisher
========================
*)

type SomeRecord = { x : int }

open JsonAssembly // import our assembler

let sendSomeRecord() =
    let send = streams.GetJsonPublisher (streams.Default()) // sending to the default exchange
    send { x = 1 } 
    // can keep sending messages

(**
Publisher is just a function that takes a message and returns when completed and 
there are two ways to obtain it:

- For long-living use, as in example above,
- Or for immediate use and disposal:

*)

let sendSomeRecordDisposeResources() =
    streams.UsingJsonPublisher
        (streams.Default())
        (fun send -> send { x = 1 }) 

    // or, using an equivalent infix operator
    { x = 2 } |-> streams.UsingJsonPublisher (streams.Default())


(** Consumer
========================
Consumer implicitly creates a queue, subscribes to a topic on the specified `Exchange` and starts listening for messages.
Consumer can bind to a `Persistent` or `Temporary` queue (temporary queues will be given Guid names, Ack messages automatically and be deleted once the consumer is garbage-collected).
For guaranteed processing you'll use Persistent queue and is expected to Ack/Nack messages explicitly.

*)
open FSharp.Data.UnitSystems.SI.UnitSymbols

let createConsumers () =
    let temp = streams.GetJsonConsumer<int> 
                    Temporary (Routed("amq.topic", "test.roundtrip"))
    let persistent = streams.GetJsonConsumer<int64> 
                        (Persistent "my_queue") (Routed("amq.topic", "test.roundtrip"))


(**
Once created, we can start polling them, specifying the timeout 
(interanally the implementation prefetches and doesn't cause a request to the server):

*)
    match temp.Get 10<s> with
    | Some r -> printf "Got an int32: %A" r.msg
    | _ -> ()

(**
The polling API may seem inefficient, but for systems that implement back-pressure this works out quite well.

Using the persistent consumer, once we have processed the message we need to acknowledge or indicate a failure otherwise: 
*)

    match persistent.Get 10<s> with
    | Some r -> printf "Got an int64: %A" r.msg
                persistent.Ack r.id
                // or persistent.Nack r.id
    | _ -> ()

(**
Consumer API is thread-safe and Get, Ack/Nack can happen in parallel. 
*)
