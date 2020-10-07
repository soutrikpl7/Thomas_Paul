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

let mutable GLOBAL_STATE = Array.empty

type MyMessage =
| GossipValue of String
| PushSumValues of int*int
| Topology of int*int //topology code, number of nodes in topolgy

let system = System.create "system" <| ConfigurationFactory.Default()



let getNodeActorID (actor: String) =
    let actorName = actor.Substring(actor.LastIndexOf '/' + 1, actor.IndexOf '#' - actor.LastIndexOf '/' - 1)
    let mutable id = 0
    for i = 0 to actorName.Length - 1 do
        if Char.IsDigit(actorName.[i]) then
            id <- id*10 + ((actorName.[i] |> int)-48)
    id

let getNeighbour(actor: String, topology: int, numNodes: int) =
    if topology = -1 then
        Console.WriteLine(sprintf "Unexpected topolgy in getNeighbour() for Actor %s" actor)
        exit(-1)
    if numNodes = -1 then
        Console.WriteLine(sprintf "Unexpected numNodes in getNeighbour() for Actor %s" actor)
        exit(-1)
    let currentActor = getNodeActorID actor
    let generator = Random()
    let mutable neighbour = generator.Next(numNodes)
    if topology = 1 then //Full Network
        while (neighbour = currentActor) do
            neighbour <- generator.Next(numNodes)
    neighbour

let nodeActorFunctionForGossip(mailbox: Actor<_>) =
    let rec loop(gossipCounter, topology, numberOfNodes) = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        match msg with
        | Topology(t, n) ->
            Console.WriteLine("Actor {0} initialised with t and n", mailbox.Self.ToString())
            return! loop(gossipCounter, t, n)
        | GossipValue(gossip) ->
            if gossipCounter < 3 then
                let neighbour = getNeighbour(mailbox.Self.ToString(), topology, numberOfNodes)
                let neighbourActor = system.ActorSelection(sprintf "akka://system/user/nodeActor%i" neighbour)
                neighbourActor <! GossipValue(gossip)
                mailbox.Self <! GossipValue(gossip)
                if mailbox.Self.ToString() <> sender.ToString() then
                    Console.WriteLine("{0} received message. Sending to {1}. Times received message: {2}", (mailbox.Self.ToString().Substring(mailbox.Self.ToString().LastIndexOf '/' + 1,
                                                                                                                mailbox.Self.ToString().IndexOf '#' - mailbox.Self.ToString().LastIndexOf '/' - 1)), neighbour, gossipCounter)
                    return! loop(gossipCounter+1, topology, numberOfNodes)
                else
                    return! loop(gossipCounter, topology, numberOfNodes)
            else
                GLOBAL_STATE.[getNodeActorID(mailbox.Self.ToString())] <- true

        | _ ->
            Console.WriteLine("Gossip node received invalid data")
            exit(-1)
        return! loop(gossipCounter, topology, numberOfNodes)
    }
    loop(0, -1, -1)

let main =
    let NUMBER_OF_NODES = 5
    GLOBAL_STATE <- Array.create NUMBER_OF_NODES false
    let message = GossipValue("Hello")
    let mutable nodeActorRef = []
    for i = 0 to NUMBER_OF_NODES-1 do
        nodeActorRef <- List.append nodeActorRef [(spawn system (sprintf "nodeActor%i" i) nodeActorFunctionForGossip)]
        (List.item i nodeActorRef) <! Topology(1, NUMBER_OF_NODES)
    (List.item 1 nodeActorRef) <! message
    while (Array.contains false GLOBAL_STATE) do
        0 |> ignore
    0

main