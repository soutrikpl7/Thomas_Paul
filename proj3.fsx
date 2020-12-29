#I @"./packages/akka.fsharp/1.4.10/lib/netstandard2.0"
#I @"./packages/akka/1.4.10/lib/netstandard2.0"
#I @"./packages/newtonsoft.json/12.0.3/lib/net45"
#I @"./packages/fspickler/5.3.2/lib/net45"
#I @"./packages/akka.testkit/1.4.10/lib/netstandard2.0"

#r "Akka.FSharp.dll"
#r "Akka.dll"
#r "Newtonsoft.Json.dll"
#r "FsPickler.dll"
#r "Akka.TestKit.dll"


open System
open Akka.Actor
open Akka.FSharp
open Akka.Configuration

let L = 16
let b = 3
let numOfSignificantDisgits = 8

let mutable nodeRefs = Array.empty
let mutable nodesAlive = Array.empty
let mutable IdToActorMapping: Map<int, IActorRef> = Map.empty
let mutable messageDeliveryCounter = 0
let mutable hops = 0

type NodeMessage =
| Initialize of int                                                 //ID of node
| RouteMessage of int * int                                         //sourceID, destID
| StartTransmission of int * int                                    //number of messages to route, number of nodes in network

let system = System.create "system" <| ConfigurationFactory.Default()

//gets unique ID for nodes
let getID (limit) = 
    let rnd = System.Random()
    let mutable value = rnd.Next(0, limit)
    while IdToActorMapping.ContainsKey(value) do
        value <- rnd.Next(0, limit)
    IdToActorMapping <- IdToActorMapping.Add(value, null)
    value

let decrementMsgCounter() =
    messageDeliveryCounter <- messageDeliveryCounter-1

//get actor reference from ID using hash map
let getActorRefFromID(ID) =
    if not (IdToActorMapping.ContainsKey(ID)) then
        Console.WriteLine("Mapping for {0} not found", ID)
        exit(-1)
    match (IdToActorMapping.TryFind(ID)) with
    | Some x ->
        x
    | _ ->
        Console.WriteLine("ID in RT not found in hash")
        exit(-1)

let nodeActorFunction(mailbox: Actor<_>) =

    let getActorIdx(actor: String) =
        let actorName = actor.Substring(actor.LastIndexOf '/' + 1, actor.IndexOf '#' - actor.LastIndexOf '/' - 1)
        let mutable idx = 0
        for i = 0 to actorName.Length - 1 do
            if Char.IsDigit(actorName.[i]) then
                idx <- idx*10 + ((actorName.[i] |> int)-48)
        idx

    let get2DArray (m, n) =
        let mutable a = Array.zeroCreate m
        for i = 0 to m-1 do
            a.[i] <- Array.create n -1
        a

    let getNeighboursIdx(node) =
        let idx = getActorIdx(node)
        let neighbourhood = Array.create L -1
        for i = 0 to L/2-1 do
            neighbourhood.[i] <- idx + i - L/2
        for i = L/2 to L-1 do
            neighbourhood.[i] <- idx + i - L/2 + 1
        for i = 0 to L-1 do
            if neighbourhood.[i] < 0 || neighbourhood.[i] > (Array.length nodeRefs) then
                neighbourhood.[i] <- -1
        neighbourhood

    let getLeafSet (nodeID) =
        let leafSet = Array.create L -1
        for i = 0 to L/2-1 do
            leafSet.[i] <- nodeID + i - L/2
        for i = L/2 to L-1 do
            leafSet.[i] <- nodeID + i - L/2 + 1
        for i = 0 to L-1 do
            if leafSet.[i] < 0 || leafSet.[i] >= (Array.length nodeRefs) then
                leafSet.[i] <- -1
        leafSet

    let getRoutingTable(nodeID) =
        let RT = get2DArray(numOfSignificantDisgits, int(2.0 ** double(b)))
        for i = 0 to numOfSignificantDisgits-1 do
            let ld = nodeID/(2.0 ** double(b * (numOfSignificantDisgits - i - 1)) |> int) % int(2.0 ** double(b))
            let mutable prefix = nodeID/(2.0 ** double(b * (numOfSignificantDisgits - i)) |> int) * (2.0 ** double(b * (numOfSignificantDisgits - i)) |> int)
            for j = 0 to int(2.0 ** double(b))-1 do
                if j < ld then
                    RT.[i].[j] <- prefix + int((2.0 ** double(b * (numOfSignificantDisgits-i-1))) - 1.0) + j*int(2.0 ** double(b * (numOfSignificantDisgits-i-1)))
                elif j = ld then
                    RT.[i].[j] <- -1
                else
                    RT.[i].[j] <- prefix + j * int(2.0 ** double(b * (numOfSignificantDisgits-i-1))) 
        RT

    let getActorRefFromID(ID) =
        if not (IdToActorMapping.ContainsKey(ID)) then
            Console.WriteLine("You are fked 1")
        match (IdToActorMapping.TryFind(ID)) with
        | Some x ->
            x
        | _ ->
            Console.WriteLine("ID in RT not found in hash")
            exit(-1)

    let reverseDigit(x) =
        let mutable copy = x
        let mutable rev = 0
        let mutable counter = 0
        while copy>0 do 
            rev <- rev * 8 + copy % 8
            counter <- counter + 1
            copy <- copy / 8

        if counter <> numOfSignificantDisgits then
            let diff = (numOfSignificantDisgits - counter) |> double
            let l10 = 8.0 ** diff //padding 0s
            rev <- rev * (l10 |> int)
        rev

    let findLongestInitialPrefix (idx1, idx2) =
        let mutable prefix = 0
        let mutable indx1 = reverseDigit idx1
        let mutable indx2 = reverseDigit idx2
        for i = 0 to numOfSignificantDisgits - 1 do
            if indx1 % 8 = indx2 % 8 then
                if prefix = i then
                    prefix <- prefix + 1
            indx1 <- indx1 / 8
            indx2 <- indx2 / 8
        prefix

    let getIdx (a: array<int>, v) =
        let mutable t = -1
        for i = 0 to (Array.length a)-1 do
            if a.[i] = v then
                t <- i
        t

    //routing function
    let getNextHop(sourceID: int, destID: int, leafSet: array<int>, neighbourhoodSet: array<int>, routingTable: array<array<int>>) =
        if Array.contains destID leafSet then                       //returns element if present in leaf set
            leafSet.[getIdx(leafSet, destID)]
        else
            let rowIdx = findLongestInitialPrefix(sourceID, destID)
            if rowIdx = numOfSignificantDisgits then                //return -1 if current node itself is the destination
                -1
            else
                let row = Array.get routingTable rowIdx             
                let p = (double(destID)/(double(0o10) ** double(numOfSignificantDisgits - rowIdx - 1)) % double(0o10)) |> int
                if row.[p] <> -1 then                               //returns best node from routing table from present
                    row.[p]
                else                                                //returns closest node from RT, leaf set o/w
                    let mutable min_d = abs(sourceID - destID)
                    let mutable nextHopNode = sourceID
                    let mutable i = 0
                    while i < (Array.length routingTable) do
                        let mutable j = 0
                        while j < (Array.length routingTable.[i]) do
                            if routingTable.[i].[j] <> -1 then
                                if (abs(routingTable.[i].[j] - destID) < min_d) then
                                    min_d <- abs(routingTable.[i].[j] - destID)
                                    nextHopNode <- routingTable.[i].[j]
                            j <- j+1
                        i <- i+1
                    i <- 0
                    while i < L do
                        if (Array.get leafSet i) <> -1 then
                            if (abs((Array.get leafSet i) - destID) < min_d) then
                                nextHopNode <- (Array.get leafSet i)
                                min_d <- abs((Array.get leafSet i) - destID)
                        i <- i+1
                    if nextHopNode <> sourceID then
                        nextHopNode
                    else
                        -1

    // let print2DArray (a: array<array<int>>, id: int) =
    //     for i = 0 to (Array.length a)-1 do
    //         for j = 0 to (Array.length a.[i])-1 do
    //             Console.Write(sprintf "%o, " a.[i].[j])
    //         Console.WriteLine()

    // let printArray (a: array<int>, id) =
    //     for i = 0 to (Array.length a)-1 do
    //         Console.Write("{0}, ", a.[i])
    //     Console.WriteLine()

    let rec loop(ID, leafSet, neighbourhoodSet, routingTable) = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        match msg with
        | Initialize(newID) ->
            let newNeighbourhoodSet = getNeighboursIdx(mailbox.Self.ToString())
            let newLeafSet = getLeafSet(newID)
            let newRoutingTable = getRoutingTable(newID)
            // print2DArray(newRoutingTable, newID)
            sender <! "true"
            return! loop(newID, newLeafSet, newNeighbourhoodSet, newRoutingTable)
        | StartTransmission (messageCount, numOfNodes) ->
            for i = 0 to messageCount-1 do
                let mutable dest = Random().Next(numOfNodes)
                while dest = ID do
                    dest <- Random().Next(numOfNodes)
                let nextHop = getNextHop(ID, dest, leafSet, neighbourhoodSet, routingTable)
                if nextHop <> -1 then
                    getActorRefFromID(nextHop) <! RouteMessage(ID, dest)
                else
                    // Console.WriteLine(sprintf "Message received at %o" ID)
                    messageDeliveryCounter <- messageDeliveryCounter-1
                    // decrementMsgCounter()
        | RouteMessage(sourceID, destID) ->
            let nextHop = getNextHop(ID, destID, leafSet, neighbourhoodSet, routingTable)
            hops <- hops+1
            if nextHop <> -1 then
                getActorRefFromID(nextHop) <! RouteMessage(sourceID, destID)
            else
                // Console.WriteLine(sprintf "Message received at node %o from node %o" sourceID destID)
                messageDeliveryCounter <- messageDeliveryCounter-1
                // decrementMsgCounter()
        return! loop(ID, leafSet, neighbourhoodSet, routingTable)
    }
    loop(-1, [||], [||], [|[||]|])

let main argv =
    hops <- 0
    let numOfNodes = ((Array.get argv 1) |> int)
    let numOfMessages = ((Array.get argv 2) |> int)
    messageDeliveryCounter <- numOfNodes * numOfMessages
    nodeRefs <- Array.zeroCreate numOfNodes
    nodesAlive <- Array.zeroCreate numOfNodes
    for i = 0 to numOfNodes-1 do
        Array.set nodeRefs i (spawn system (sprintf "nodeActor%i" i) nodeActorFunction)
    for i = 0 to numOfNodes-1 do
        let ID = getID(numOfNodes)
        IdToActorMapping <- IdToActorMapping.Add(ID, nodeRefs.[i])
        nodesAlive.[i] <- (nodeRefs.[i] <? Initialize(ID))
    for i = 0 to numOfNodes-1 do
        let b = Async.RunSynchronously(nodesAlive.[i], 1000) |> string
        0 |> ignore
    for i = 0 to numOfNodes-1 do
        nodeRefs.[i] <! StartTransmission(numOfMessages, numOfNodes)
    // while messageDeliveryCounter > 0 do
    //     0 |> ignore
    Console.ReadKey() |> ignore
    Console.WriteLine("Hops: {0}", hops)
    let deno = (numOfMessages * numOfNodes)|>double
    Console.WriteLine(sprintf "Average Hops per route: %f" ((hops|>double) / deno))

main fsi.CommandLineArgs