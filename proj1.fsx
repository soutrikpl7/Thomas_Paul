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
open System.Numerics
open Akka.FSharp
open Akka.Configuration
open Akka.Routing
open Akka.TestKit
open System.Diagnostics

#time "on"
let mutable FINISHED_FLAG = 0
let mutable count = 0

type MyMessage =
| Type1 of int*int
| Type2 of int

let encode N k =
    sprintf "%i %i" N k

let getN s =
    s.ToString().Substring(0, s.ToString().IndexOf(' '))

let getK s =
    s.ToString().Substring(s.ToString().IndexOf(' ')+1, s.ToString().Length - s.ToString().IndexOf(' ') - 1)

let workerFunction (mailbox: Actor<_>) =
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        match msg with
        | Type1(N,k) ->
            let mutable sum = 0.0 |> double
            let mutable prev_sum = 0.0 |> double
            for i = N to (N+k-1) do
                prev_sum <- sum
                sum <- sum + ((i |> double) ** 2.0)
                if sum < prev_sum then
                    Console.WriteLine(sprintf "Overflow in %i" N)
                    sender <! Type2(0)
                    return! loop()

            if floor (sqrt sum) = sqrt sum then
                sender <! Type2(N)
            else
                sender <! Type2(0)
        | _ ->
            Console.WriteLine("Invalid input in worker")
        return! loop()
    }
    loop()


let bossActorFunction (mailbox: Actor<_>) =
    let rec loop() = actor {
        let! rcv = mailbox.Receive()

        match rcv with
        | Type1(N,k) ->
            let mutable c = 0
            let workerActorRef =
                [0 .. 3]
                |> List.map(fun id -> spawn mailbox (sprintf "workerActor%i" id) workerFunction)
            for i = 1 to N do
                if i%4 = 0 then
                    c <- 0
                (List.item (c) workerActorRef) <! Type1(i, k)
                c <- c+1
        | Type2(x) ->
            if x<>0 then
                Console.Write(sprintf "%i\n" x)
                // printf "%i, " x
            count <- count - 1
            if count = 0 then
                FINISHED_FLAG <- 1
        return! loop()
    }
    loop()

let main argv =
    let N = ((Array.get argv 1) |> int)
    let k = ((Array.get argv 2) |> int)
    count <- N
    let msgToBoss = Type1(N, k)

    let system = System.create "system" <| ConfigurationFactory.Default()
    let bossActor = spawn system "boss_actor" bossActorFunction
    bossActor <! msgToBoss
    while(FINISHED_FLAG = 0) do
        0|>ignore
    system.Terminate() |> ignore
    0


main fsi.CommandLineArgs