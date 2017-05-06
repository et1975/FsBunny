(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../build_output"

(**
Streaming API for RabbitMQ
======================
FsBunny implements a streaming API on top of official RabbitMQ client for implementation of event-driven systems.

The core idea is that while there are many streams carrying many messages using many serializers, each stream is dedicated to a single type of message serialized using certain serializer. 

It works under assumptions that:

- we never want to loose a message
- the exchange + topic tells us the message type (as well as serialization format)
- the consumer is long-lived and handles only one type of message
- the consumer decides when to pull the next message of a queue
- the publishers can be long- or short- lived and address any topic
- we have multiple serialization formats and may want to add new ones easily


Installing
======================

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The FsBunny library can be <a href="https://nuget.org/packages/FsBunny">installed from NuGet</a>:
      <pre>PM> Install-Package FsBunny</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Example
-------

This example demonstrates the functional language defined by FsBunny:

*)
#r "FsBunny.dll"
#r "RabbitMQ.Client.dll"
open FsBunny
open FSharp.Data.UnitSystems.SI.UnitSymbols

let ``RabbitMQ raw event roundtrips``() = 
    let streams = 
           RabbitMqEventStreams(
              RabbitMQ.Client.ConnectionFactory(), // underlying connection factory
              "amq.topic",                         // default exchange
              3us,                                 // number of reconnect retries (publisher only)
              2000us) :> EventStreams              // prefetch limit
    
    let consumer = streams.GetConsumer<int> 
                    Temporary 
                    (Routed("amq.topic", "test.*.sample")) 
                    (fun (topic,headers,payload) -> int(topic.Split('.').[1]))
    let publish = streams.GetPublisher<int> 
                    (fun x -> Routed("amq.topic", sprintf "test.%d.sample" x),None,[||])
    
    publish 1

    match consumer.Get 10<s> with
    | Some r -> printf "Got 1: %A" (r.msg = 1)
    | _ -> failwith "should have gotten the message we just sent"

(**

Samples & documentation
-----------------------


 * [Tutorial](tutorial.html) goes into more details.

 * [API Reference](reference/index.html) contains automatically generated documentation for all types, modules
   and functions in the library. This includes additional brief samples on using most of the
   functions.
 
Contributing and copyright
--------------------------
The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. 

The library is available under Apache license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/Prolucid/FsBunny/tree/master/docs/content
  [gh]: https://github.com/Prolucid/FsBunny
  [issues]: https://github.com/Prolucid/FsBunny/issues
  [readme]: https://github.com/Prolucid/FsBunny/blob/master/README.md
  [license]: https://github.com/Prolucid/FsBunny/blob/master/LICENSE.md


Copyright 2017 Prolucid Technologies Inc
*)
