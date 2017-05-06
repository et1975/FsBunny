namespace FsBunny.Assemblers

open FsBunny
open System.Text.RegularExpressions
open System

[<AutoOpen>]
module MqttAssembly =
    let private toRegex routingKey =
        Regex(Regex.Escape(routingKey).Replace(@"\*","([^.]+)"), RegexOptions.Compiled)

    let sysBrokerStatusAssembler routingKey =
        let rx = toRegex routingKey
        fun (topic,_,bytes:byte array) ->
            let m = rx.Match(topic)
            if m.Success then
                m.Groups.[1].Value,bytes.[0]
            else
                raise(ArgumentException("No match for: "+topic))


    type FsBunny.EventStreams with 
        /// Construct a consumer, using specified message type, queue, the exchange to bind to and Proto assember.
        member this.GetMqttStatusConsumer routingKey (queue: Queue) (exchange:Exchange) =
            this.GetConsumer queue exchange (sysBrokerStatusAssembler routingKey)
