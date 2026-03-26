namespace FsBunny

open System
open RabbitMQ.Client

/// RabbitMqEventStreams constructs event-stream publishers and consumers.
/// factory: RabbitMQ connection factory to use.
/// defaultExchange: default exchange to bind to.
/// retries: number of reconnect attempts.
/// limit: prefetch limit.
type RabbitMqEventStreams(factory : ConnectionFactory, defaultExchange : string, retries : uint16, limit:uint16) = 
    let connectionRef = ref (Option<IConnection>.None)
    let withConnection cont =
        fun () -> 
            match connectionRef.Value with
            | Some c when c.IsOpen -> c
            | _ -> connectionRef.Value <- Some(factory.CreateConnection())
                   connectionRef.Value.Value
        |> lock connectionRef
        |> cont

    let openChannel() =
        let channelRef = ref (Option<IModel>.None)
        (fun cont ->
            match channelRef.Value with
            | Some channel when channel.IsOpen -> channel
            | _ -> channelRef.Value <- Some (withConnection <| fun conn -> conn.CreateModel())
                   channelRef.Value.Value
            |> cont
        ,fun () -> channelRef.Value.Value.Dispose())

    let withBinding exchange queue = 
        let bindingRef = ref Option<string*IModel>.None
        fun cont -> 
            match bindingRef.Value with
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
                bindingRef.Value <- Some(q.QueueName, channel)
                bindingRef.Value.Value
            |> cont
    
    new(factory : ConnectionFactory, defaultExchange : string) = new RabbitMqEventStreams(factory, defaultExchange, 3us, 300us)

    interface EventStreams with
        member x.Default() = Direct defaultExchange
        member x.Routed topic = Routed(defaultExchange, topic)
        member x.GetPublisher<'T> disassembler : Publisher<'T> = 
            openChannel()
            |> fst
            |> Publisher.buildPublisher retries disassembler

        member x.GetConsumer<'T> queue exchange assember: Consumer<'T> = 
            withBinding exchange queue
            |> Consumer.buildConsumer queue.AutoAck assember

        member x.UsingPublisher<'T> disassembler (cont:Publisher<'T> -> unit) = 
            let (withChannel,dispose) = openChannel()
            withChannel
            |> Publisher.buildPublisher retries disassembler
            |> cont 
            dispose()

        member x.Dispose() =        
            match connectionRef.Value with
            | Some conn ->
                conn.Dispose()
                connectionRef.Value <- None
            | _ -> ()
