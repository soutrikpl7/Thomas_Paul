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
open Akka.FSharp
open Akka.Configuration

let mutable GLOBAL_STATE = Array.empty //boolean array for convergence state of a node
let GOSSIP_THRESHOLD = 3

type MyMessage =
| GossipValue of String
| PushSumValues of double*double
| Topology of int*int //topology code, number of nodes in topolgy

let system = System.create "system" <| ConfigurationFactory.Default()

//accepts string of the form "akka://system/user/nodeActorID#xxxxxxx" and returns the ID
let getNodeActorID (actor: String) =
    let actorName = actor.Substring(actor.LastIndexOf '/' + 1, actor.IndexOf '#' - actor.LastIndexOf '/' - 1)
    let mutable id = 0
    for i = 0 to actorName.Length - 1 do
        if Char.IsDigit(actorName.[i]) then
            id <- id*10 + ((actorName.[i] |> int)-48)
    id

//accepts string of the form "akka://system/user/nodeActorID#xxxxxxx" and returns a string as nodeActorID
let getNodeActorName (actor: String) =
    actor.Substring(actor.LastIndexOf '/' + 1, actor.IndexOf '#' - actor.LastIndexOf '/' - 1)

//generates neighburs for a node in a full network
let getNeighboursFullNetwork (node, numberOfNodes) =
    let mutable neighbours = Array.zeroCreate (numberOfNodes-1)
    let mutable c = 0
    for i = 0 to numberOfNodes-1 do
        if i <> node then
            Array.set neighbours c i
            c <- c + 1
    neighbours

//generates neighburs for a node in a 2D network
let getNeighbours2DGrid (node, numberOfNodes) =
    let sq = sqrt (numberOfNodes |> float) |> floor |> int
    let mutable neighbours = [|node-sq; node-1; node+1; node+sq|] //neighbour above, left, right, and below respectively in square
    if (Array.get neighbours 0) < 0 then
        Array.set neighbours 0 -1
    if (node%sq = 0) || ((Array.get neighbours 1) < 0) then
        Array.set neighbours 1 -1
    if ((node+1)%sq = 0) || (node+1 >= numberOfNodes) then
        Array.set neighbours 2 -1
    if (Array.get neighbours 3) >= numberOfNodes then
        Array.set neighbours 3 -1
    neighbours

//generates neighburs for a node in a line network
let getNeighboursLine (node, numberOfNodes) =
    let mutable neighbours = [|node-1; node+1|]
    if (Array.get neighbours 0) < 0 then
        Array.set neighbours 0 -1
    if (Array.get neighbours 1) >= numberOfNodes then
        Array.set neighbours 1 -1
    neighbours

//generates neighburs for a node in a imperfect 2D network
let getNeighboursImp2D (node, numberOfNodes) =
    if numberOfNodes < 5 then
        getNeighbours2DGrid (node, numberOfNodes)
    else
        let sq = sqrt (numberOfNodes |> float) |> floor |> int
        let mutable randNeighbour = Random().Next(numberOfNodes)
        while randNeighbour = node || randNeighbour = (node-sq) || randNeighbour = (node-1) || randNeighbour = (node+1) || randNeighbour = (node+sq) do
            randNeighbour <- Random().Next(numberOfNodes)
        let mutable neighbours = [|node-sq; node-1; node+1; node+sq; randNeighbour|]
        if (Array.get neighbours 0) < 0 then
            Array.set neighbours 0 -1
        if (node%sq = 0) || ((Array.get neighbours 1) < 0) then
            Array.set neighbours 1 -1
        if ((node+1)%sq = 0) || (node+1 >= numberOfNodes) then
            Array.set neighbours 2 -1
        if (Array.get neighbours 3) >= numberOfNodes then
            Array.set neighbours 3 -1
        neighbours

//returns true if node has already converged or if the node doesn't exist; false o/w
let isConverged (node) =
    if node = -1 then
        true
    else
        GLOBAL_STATE.[node]

//returns true if ALL nodes passed to it are converged; false o/w
let areAllNeighboursConverged (neighbours) =
    let mutable f = true
    for i = 0 to (Array.length neighbours)-1 do
        if neighbours.[i] = -1 then
            0 |> ignore
        elif not (GLOBAL_STATE.[neighbours.[i]]) then
            f <- false
    f

//returns an unconverged node from the list passed to it. returns -1 if no such node exists
let getUnconvergerNeighbour (neighbours) =
    let mutable neighbour = Array.get neighbours (Random().Next(Array.length neighbours))
    while neighbour=(-1) || (not (areAllNeighboursConverged(neighbours)) && isConverged(neighbour)) do
        neighbour <- Array.get neighbours (Random().Next(Array.length neighbours))
    
    if neighbour=(-1) then
        -1
    elif not (areAllNeighboursConverged(neighbours)) && not (isConverged(neighbour)) then
        neighbour
    else
        -1

//returns an unconverged node in the network. returns -1 if no such node exists
let getUnconvergedNode (state_array) =
    let mutable nextNode = -1
    for i = 0 to (Array.length state_array)-1 do
        if not(Array.get state_array i) then
            nextNode <- i
    nextNode

//returns true if ALL nodes in network are converged. false o/w
let areAllNodesConverged (state_array) =
    not (Array.contains false state_array)

//function for a node/actor in gossip
let nodeActorFunctionForGossip(mailbox: Actor<_>) =
    let rec loop(gossipCounter, neighbours) = actor {
        let! msg = mailbox.Receive()
        match msg  with
        | Topology(top, num) -> //initialising node with neighbours
            match top with
            | 1 -> return! loop(0, getNeighboursFullNetwork(getNodeActorID(mailbox.Self.ToString()), num))
            | 2 -> return! loop(0, getNeighbours2DGrid(getNodeActorID(mailbox.Self.ToString()), num))
            | 3 -> return! loop(0, getNeighboursLine(getNodeActorID(mailbox.Self.ToString()), num))
            | 4 -> return! loop(0, getNeighboursImp2D(getNodeActorID(mailbox.Self.ToString()), num))
            | _ ->
                Console.WriteLine("{0} received invalid topology during initialisation", mailbox.Self.ToString())
                exit(-1)
        | GossipValue(data) -> //message received in node
            if gossipCounter + 1 > GOSSIP_THRESHOLD then //convergence condition achieved for node
                Array.set GLOBAL_STATE (getNodeActorID(mailbox.Self.ToString())) true
            if not(areAllNeighboursConverged(neighbours)) then //passing gossip to a random neighbour
                let neighbour = getUnconvergerNeighbour(neighbours)
                if neighbour <> -1 then
                    let neighbourActor = system.ActorSelection(sprintf "akka://system/user/nodeActor%i" neighbour)
                    neighbourActor <! GossipValue(data)
            elif not(areAllNodesConverged(GLOBAL_STATE)) then //choosing a node in the network when all neighbours are converged
                let nextNode = getUnconvergedNode(GLOBAL_STATE)
                if nextNode <> -1 then
                    let nextActor = system.ActorSelection(sprintf "akka://system/user/nodeActor%i" nextNode)
                    nextActor <! GossipValue(data)
            else //convergence for the last unconverged node
                Array.set GLOBAL_STATE (getNodeActorID(mailbox.Self.ToString())) true
            return! loop(gossipCounter + 1, neighbours)
        | _ ->
            Console.WriteLine("Gossip node received invalid message")
            exit(-1)
    }
    loop(0, [||])

//function to start the gossip algorithm
let startGossip (numberOfNodes, topology) =
    let mutable nodeActorRef = []
    for i = 0 to numberOfNodes-1 do
        nodeActorRef <- List.append nodeActorRef [(spawn system (sprintf "nodeActor%i" i) nodeActorFunctionForGossip)]
        (List.item i nodeActorRef) <! Topology(topology, numberOfNodes)
    let t1 = System.Diagnostics.Stopwatch.StartNew()
    let randNode = (Random().Next(numberOfNodes))
    (List.item randNode nodeActorRef) <! GossipValue("Hello!")
    while (Array.contains false GLOBAL_STATE) do
        0 |> ignore
    t1.Stop()
    Console.WriteLine("Time for convergence {0} milliseconds", t1.Elapsed.TotalMilliseconds)

//function for a node/actor in push-sum
let nodeActorFunctionForPushSum(mailbox: Actor<_>) =
    let rec loop(s, w, convergenceCounter, neighbours) = actor {
        let! msg = mailbox.Receive()
        match msg with
        | Topology(top, num) -> //initialising node with neighbours
            match top with
            | 1 -> return! loop(getNodeActorID(mailbox.Self.ToString()) |> double, 1.0, 0, getNeighboursFullNetwork(getNodeActorID(mailbox.Self.ToString()), num))
            | 2 -> return! loop(getNodeActorID(mailbox.Self.ToString()) |> double, 1.0, 0, getNeighbours2DGrid(getNodeActorID(mailbox.Self.ToString()), num))
            | 3 -> return! loop(getNodeActorID(mailbox.Self.ToString()) |> double, 1.0, 0, getNeighboursLine(getNodeActorID(mailbox.Self.ToString()), num))
            | 4 -> return! loop(getNodeActorID(mailbox.Self.ToString()) |> double, 1.0, 0, getNeighboursImp2D(getNodeActorID(mailbox.Self.ToString()), num))
            | _ ->
                Console.WriteLine("{0} received invalid topology during initialisation", mailbox.Self.ToString())
                exit(-1)
        | PushSumValues(s_in, w_in) ->
            let local_s = s + s_in //updating a local actors s & w values using the new values it got
            let local_w = w + w_in
            let DIFF_THRESHOLD = 10.0 ** -10.0 //accuracy for convergence
            let ratioDifference = abs (s/w - local_s/local_w)
            if convergenceCounter >= 2 then //convergence condition achieved for node
                Array.set GLOBAL_STATE (getNodeActorID(mailbox.Self.ToString())) true
            if not(areAllNeighboursConverged(neighbours)) then //half of s & w values passed to a random neighbour
                let neighbour = getUnconvergerNeighbour(neighbours)
                if neighbour <> -1 then
                    let neighbourActor = system.ActorSelection(sprintf "akka://system/user/nodeActor%i" neighbour)
                    neighbourActor <! PushSumValues(local_s/2.0, local_w/2.0)
                if ratioDifference < DIFF_THRESHOLD then
                    return! loop(local_s/2.0, local_w/2.0, convergenceCounter + 1, neighbours) //actor is recalled by incrementing convergenceCounter when difference of ratios is less than fixed threshold
            elif not (areAllNodesConverged(GLOBAL_STATE)) then //half of s & w values passed to a random node in network if all neighbours are converged
                let nextNode = getUnconvergedNode (GLOBAL_STATE)
                if nextNode <> -1 then
                    let nextNodeActor = system.ActorSelection(sprintf "akka://system/user/nodeActor%i" nextNode)
                    nextNodeActor <! PushSumValues(local_s/2.0, local_w/2.0)
                if ratioDifference < DIFF_THRESHOLD then
                    return! loop(local_s/2.0, local_w/2.0, convergenceCounter + 1, neighbours) //actor is recalled by incrementing convergenceCounter when difference of ratios is less than fixed threshold
            else
                Array.set GLOBAL_STATE (getNodeActorID(mailbox.Self.ToString())) true
            return! loop(local_s/2.0, local_w/2.0, 0, neighbours) //actor is recalled with convergenceCounter=0 as threshold condition was not met
        | _ ->
            Console.WriteLine("Push Sum node received invalid message")
            exit(-1)
    }
    loop(0.0, 0.0, 0, [||])

//function to start the push-sum algorithm
let startPushSum (numberOfNodes, topology) =
    let mutable nodeActorRef = []
    for i = 0 to numberOfNodes-1 do
        nodeActorRef <- List.append nodeActorRef [(spawn system (sprintf "nodeActor%i" i) nodeActorFunctionForPushSum)]
        (List.item i nodeActorRef) <! Topology(topology, numberOfNodes)
        
    let t1 = System.Diagnostics.Stopwatch.StartNew()
    let randNode = (Random().Next(numberOfNodes))
    (List.item randNode nodeActorRef) <! PushSumValues(randNode |> double, 1.0)
    while (Array.contains false GLOBAL_STATE) do
        0 |> ignore
    t1.Stop()
    Console.WriteLine("Time for convergence {0} milliseconds", t1.Elapsed.TotalMilliseconds)

let main argv =
    let numberOfNodes = ((Array.get argv 1) |> int)
    GLOBAL_STATE <- Array.create numberOfNodes false
    let topolgy =
        match (((Array.get argv 2) |> string).ToLower()) with
        | "full" -> 1
        | "2d" -> 2
        | "line" -> 3
        | "imp2d" -> 4
        | _ ->
            Console.WriteLine("Invalid topology input")
            exit(-1)
    match (((Array.get argv 3) |> string).ToLower()) with
    | "gossip" -> startGossip(numberOfNodes, topolgy)
    | "push-sum" -> startPushSum(numberOfNodes, topolgy)
    | _ ->
        Console.WriteLine("Invalid algorithm input")
        exit(-1)
    0

main fsi.CommandLineArgs