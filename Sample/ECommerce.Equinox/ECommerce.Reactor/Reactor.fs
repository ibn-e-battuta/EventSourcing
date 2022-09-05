module ECommerce.Reactor.Reactor

open ECommerce.Domain
open ECommerce.Infrastructure // Exception
open Metrics

/// Gathers stats based on the outcome of each Span processed for emission, at intervals controlled by `StreamsConsumer`
type Stats(log, statsInterval, stateInterval, verboseStore, ?logExternalStats) =
    inherit Propulsion.Streams.Stats<Outcome>(log, statsInterval, stateInterval)

    let mutable ok, skipped, na = 0, 0, 0

    override _.HandleOk res =
        observeReactorOutcome res
        match res with
        | Outcome.Ok (used, unused) -> ok <- ok + used; skipped <- skipped + unused
        | Outcome.Skipped count -> skipped <- skipped + count
        | Outcome.NotApplicable count -> na <- na + count
    override _.HandleExn(log, exn) =
        Exception.dump verboseStore log exn

    override _.DumpStats() =
        if ok <> 0 || skipped <> 0 || na <> 0 then
            log.Information(" used {ok} skipped {skipped} n/a {na}", ok, skipped, na)
            ok <- 0; skipped <- 0; na <- 0
        match logExternalStats with None -> () | Some f -> f Serilog.Log.Logger
        base.DumpStats()

let isReactionStream = function
    | struct (ShoppingCart.Category, _) -> true
    | _ -> false

let handle
        (cartSummary : ShoppingCartSummaryHandler.Service)
        (confirmedCarts : ConfirmedHandler.Service)
        struct (stream, span) = async {
    match stream, span with
    | ShoppingCart.Reactions.Parse (cartId, events) ->
        match events with
        | ShoppingCart.Reactions.Confirmed ->
            let! _done = confirmedCarts.TrySummarizeConfirmed(cartId) in ()
        | _ -> ()
        match events with
        | ShoppingCart.Reactions.StateChanged ->
            let! worked, version' = cartSummary.TryIngestSummary(cartId)
            let outcome = if worked then Outcome.Ok (1, Array.length span - 1) else Outcome.Skipped span.Length
            return struct (Propulsion.Streams.SpanResult.OverrideWritePosition version', outcome)
        | _ -> return Propulsion.Streams.SpanResult.AllProcessed, Outcome.NotApplicable span.Length
    | x -> return failwith $"Invalid event %A{x}" } // should be filtered by isReactionStream
//    | _ -> return Propulsion.Streams.SpanResult.AllProcessed, Outcome.NotApplicable span.events.Length }

type Config private () =

    let create (sourceStore, targetStore) =
        let cartSummary = ShoppingCartSummaryHandler.Config.create (sourceStore, targetStore)
        let confirmedCarts = ConfirmedHandler.Config.create (sourceStore, targetStore)
        handle cartSummary confirmedCarts

    static member StartSink(log : Serilog.ILogger, stats : Stats,
                            handle : struct (FsCodec.StreamName * Propulsion.Streams.Default.StreamSpan) ->
                                     Async<struct (Propulsion.Streams.SpanResult * Outcome)>,
                            maxReadAhead : int, maxConcurrentStreams : int, ?wakeForResults, ?idleDelay, ?purgeInterval) =
        Propulsion.Streams.Default.Config.Start(log, maxReadAhead, maxConcurrentStreams, handle, stats, stats.StatsInterval.Period,
                                                ?wakeForResults = wakeForResults, ?idleDelay = idleDelay, ?purgeInterval = purgeInterval)

    static member StartSource(log, sink, sourceConfig, filter) =
        Args.SourceConfig.start (log, Config.log) sink filter sourceConfig
