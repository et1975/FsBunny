module FsBunny.Publisher
open RabbitMQ.Client
open System
    
let buildPublisher retries disassembler withChannel = 
    let rec attemptSend n (exchange,headers,smsg:byte[]) = 
        try 
            withChannel
            <| fun (channel:IModel) ->
                    let props = channel.CreateBasicProperties()
                    props.DeliveryMode <- 2uy // persistent mode
                    match headers with
                    | Some h -> props.Headers <- h
                    | _ -> ()
                    let body = ReadOnlyMemory(smsg)
                    match exchange with
                    | Direct name -> channel.BasicPublish(name, null, props, body)
                    | Routed(name, topic) -> channel.BasicPublish(name, topic, props, body)
        with ex -> 
            if n > 0us then attemptSend (n - 1us) (exchange,headers,smsg)
            else reraise()
    disassembler >> attemptSend retries
