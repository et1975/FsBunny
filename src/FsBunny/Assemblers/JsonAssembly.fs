namespace FsBunny.Assemblers

open FsBunny
open System.Text
open System
open System.IO
open Newtonsoft.Json

[<AutoOpen>]
module JsonAssembly =
    let serializer = 
        let s = JsonSerializer.CreateDefault()
        s.Converters.Add(Fable.JsonConverter())
        s.Converters.Add(Converters.IsoDateTimeConverter (DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fff'Z'"))
        s

    let disassembler exchange (item:'T) =
        use ms = new MemoryStream()
        use sw = new StreamWriter (ms)
        use jw = new JsonTextWriter (sw)
        serializer.Serialize (jw,item)
        jw.Flush()
        exchange, None, ms.ToArray()

    let assembler (_,_,bytes:byte[]) : 'T =
        use ms = new MemoryStream(bytes)
        use sr = new StreamReader (ms)
        use jr = new JsonTextReader (sr)
        serializer.Deserialize<'T>(jr)

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
