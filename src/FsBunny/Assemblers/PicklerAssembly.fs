namespace FsBunny.Assemblers

open MBrace.FsPickler
open System.IO
open FsBunny

[<AutoOpen>]
module PicklerAssembly =
    let private serializer = FsPickler.CreateBinarySerializer()

    let disassembler exchange item =
        use ms = new MemoryStream()
        serializer.Serialize(ms, item)
        exchange, None, ms.GetBuffer()

    let assembler (_,_,bytes:byte[]) =
        use ms = new MemoryStream(bytes)
        serializer.Deserialize(ms)

    type global.FsBunny.EventStreams with 
        /// Construct a consumer, using specified message type, queue, the exchange to bind to and Pickler assember.
        member this.GetPicklerConsumer<'T> (queue: Queue) (exchange:Exchange) : Consumer<'T> =
            this.GetConsumer<'T> queue exchange assembler

        /// Construct a publisher for the specified message type using Pickler disassembler.
        member this.GetPicklerPublisher<'T> (exchange: Exchange) : Publisher<'T> = 
            this.GetPublisher<'T> (disassembler exchange)

        /// Use a publisher with a continuation for the specified message type using Pickler disassembler.
        member this.UsingPicklerPublisher<'T> (exchange: Exchange) (cont:Publisher<'T> -> unit) : unit =
            this.UsingPublisher<'T> (disassembler exchange) cont