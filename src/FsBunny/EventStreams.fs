namespace FsBunny

open System
open RabbitMQ.Client
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

/// RabbitMqEventStreams constructs event-stream publishers and consumers.
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

