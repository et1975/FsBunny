/// Consumer Implementation
[<RequireQualifiedAccessAttribute>]
module FsBunny.Consumer 

open RabbitMQ.Client
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
            let rec consume remaining fraction =
                match withConsumer (fun consumer -> consumer.Queue.Dequeue fraction) with
                | true, evt -> 
                    try 
                        Some { msg = assember(evt.RoutingKey,evt.BasicProperties.Headers,evt.Body)
                               id = evt.DeliveryTag }
                    with ex -> // dead letter policy should be used to handle permanently rejected/undeliverable: http://www.rabbitmq.com/dlx.html
                        withConsumer (fun consumer -> consumer.Model.BasicNack(evt.DeliveryTag, false, false))
                        None
                | false, _ when remaining > 0 -> consume (remaining-fraction) fraction
                | _ -> None
            consume (int timeout*1000) (int timeout * 10)
        member x.Ack id = withConsumer <| fun consumer -> consumer.Model.BasicAck(id, false)
        member x.Nack id = withConsumer <| fun consumer -> consumer.Model.BasicNack(id, false, true)
    }

/// Convert auto-acking consumer into ISeq<_>
/// selector: result mapper.
/// consumer: event stream consumer to use
let toSeq selector (consumer : Consumer<'T>) = 
    seq {
        while true do
            yield selector <| consumer.Get
    }
