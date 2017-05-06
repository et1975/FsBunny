namespace FsBunny.Assemblers

open FsBunny
open Google.Protobuf

[<AutoOpen>]
module ProtoAssembly =
    let disassembler exchange (item:#IMessage<'T>) =
        exchange, None, item.ToByteArray()

    let assembler<'T when 'T :> IMessage<'T> and 'T:(new:'T)> (parser : MessageParser<'T>) =
        fun (_,_,bytes:byte[]) -> parser.ParseFrom(bytes)

    type FsBunny.EventStreams with 
        /// Construct a consumer, using specified message type, queue, the exchange to bind to and Proto assember.
        member this.GetProtoConsumer<'T when 'T :> IMessage<'T> and 'T:(new:'T)> (queue: Queue) (exchange:Exchange) : Consumer<'T> =
            let parser = MessageParser<'T>(fun () -> new 'T())
            this.GetConsumer<'T> queue exchange (assembler parser)

        /// Construct a publisher for the specified message type using Proto disassembler.
        member this.GetProtoPublisher<'T when 'T :> IMessage<'T>> (exchange: Exchange) : Publisher<'T> = 
            this.GetPublisher<'T> (disassembler exchange)

        /// Use a publisher with a continuation for the specified message type using Proto disassembler.
        member this.UsingProtoPublisher<'T when 'T :> IMessage<'T>> (exchange: Exchange) (cont:Publisher<'T> -> unit) : unit =
            this.UsingPublisher<'T> (disassembler exchange) cont