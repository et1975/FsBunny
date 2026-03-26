/// Consumer Implementation
[<RequireQualifiedAccessAttribute>]
module FsBunny.Consumer 

open RabbitMQ.Client
open RabbitMQ.Client.Events
open System.Collections.Generic
open System
open System.Collections.Concurrent

let buildConsumer autoAck assember withBinding = 
    let channelRef = ref Option<IModel>.None
    let queue = new BlockingCollection<BasicDeliverEventArgs>()
    let withChannel cont = 
        fun () ->
            match channelRef.Value with
            | Some channel when channel.IsOpen -> channel
            | _ -> fun (name, channel:IModel) ->
                        let consumer = EventingBasicConsumer(channel)
                        consumer.Received.Add(fun evt -> queue.Add(evt))
                        channelRef.Value <- Some channel
                        channel.BasicConsume(name, autoAck, consumer) |> ignore
                        channel
                   |> withBinding
        |> lock channelRef
        |> cont

    withChannel ignore // force the subscription to occur

    { new Consumer<'T> with
        member x.Get timeout =
            let mutable evt = Unchecked.defaultof<BasicDeliverEventArgs>
            if queue.TryTake(&evt, int timeout*1000) then
                try 
                    Some { msg = assember(evt.RoutingKey,evt.BasicProperties.Headers,evt.Body.ToArray())
                           id = evt.DeliveryTag }
                with ex -> // dead letter policy should be used to handle permanently rejected/undeliverable: http://www.rabbitmq.com/dlx.html
                    withChannel (fun channel -> channel.BasicNack(evt.DeliveryTag, false, false))
                    None
            else None
        member x.Ack id = withChannel <| fun channel -> channel.BasicAck(id, false)
        member x.Nack id = withChannel <| fun channel -> channel.BasicNack(id, false, true)
        member x.Dispose() =
            match channelRef.Value with
            | Some ch -> 
               ch.Dispose()
               channelRef.Value <- None
            | _ -> ()
            queue.Dispose()
    }

/// Convert auto-acking consumer into ISeq<_>
/// selector: result mapper.
/// consumer: event stream consumer to use
let toSeq selector (consumer : Consumer<'T>) = 
    seq {
        while true do
            yield selector <| consumer.Get
    }
