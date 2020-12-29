
#load "bootstrap.fsx"
#load "DS.fsx"

open System
open Akka.FSharp
open Akka.Configuration
open Newtonsoft.Json
open DS

open System.Text
open System.Net.WebSockets
open System.Threading

let system = System.create "FSServer" <| ConfigurationFactory.Default()
let socket = new ClientWebSocket()
let cts = new CancellationTokenSource()
let uri = Uri("ws://localhost:9001/init")
socket.ConnectAsync(uri, cts.Token).Wait()

let rec receiveMessageFromServer () =
    let buffer = WebSocket.CreateClientBuffer(10000, 10000)
    let receiveTask = socket.ReceiveAsync(buffer, cts.Token)
    receiveTask.Wait()
    let msg = Encoding.ASCII.GetString((Seq.toArray buffer), 0, (receiveTask.Result.Count))
    // Console.WriteLine("MSG: {0}", msg)
    let m = JsonConvert.DeserializeObject<DS.Message>(msg)
    if m.Operation = "TweetNotification" then
        Console.WriteLine("{0} just tweeted: {1}", m.User, m.Payload)
        receiveMessageFromServer ()
    elif m.Operation = "DisplayTweets" then
        let tweetList = JsonConvert.DeserializeObject<Tweet list>(m.Payload)
        if not (List.isEmpty tweetList) then
            for i = 0 to (List.length tweetList)-1 do
                Console.WriteLine("({0})\n{1} tweeted: {2}",(i+1) , tweetList.[i].Author, tweetList.[i].Message)
            Console.WriteLine("Enter tweet number of tweet to retweet, or -1 to go back")
            let cc = Console.ReadLine() |> int
            if 0 < cc && cc <= (List.length tweetList) then
                let response = JsonConvert.SerializeObject(DS.Message(m.User, "Retweet", tweetList.[cc-1].Message)) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
                socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
            // Thread.Sleep(1000)
            else
                let clientActorRef = system.ActorSelection ("akka://FSServer/user/client_actor")
                clientActorRef <! JsonConvert.SerializeObject (DS.Message ("", "init", ""))
        else
            Console.WriteLine("No tweets to display")
            let clientActorRef = system.ActorSelection ("akka://FSServer/user/client_actor")
            clientActorRef <! JsonConvert.SerializeObject (DS.Message ("", "init", ""))
        receiveMessageFromServer ()
    elif m.Operation = "Logout" then
        Console.WriteLine("Logged out")
        socket.CloseAsync (WebSocketCloseStatus.Empty, "", cts.Token) |> ignore
        0
    else
        let clientActorRef = system.ActorSelection ("akka://FSServer/user/client_actor")
        clientActorRef <! msg
        receiveMessageFromServer ()


let clientActorFunction (mailbox: Actor<_>) =

    let rec promptUserAction (userName: string, password: string) =
        //1-Register; 2-log in; 3-make tweet; 4-search my mentions; 5-search by following; 6-add following; 7-search by tag; 8-logout
        Console.WriteLine("1: Register\n2: Log in\n3: Make a tweet\n4: Search my mentions\n5: Search tweets made by subscribers\n6: Follow a user\n7: Search tweets by #tag\n8: Log out")
        Console.WriteLine("Enter op code")
        let mutable ip = Console.ReadLine()
        if ip = "1" then
            Console.WriteLine ("Enter username and password")
            let uname = Console.ReadLine()
            let pass = Console.ReadLine()
            let response = JsonConvert.SerializeObject(DS.Message("", "Register", JsonConvert.SerializeObject(Authorise(uname, pass)))) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "2" then
            Console.WriteLine ("Enter username and password")
            let uname = Console.ReadLine()
            let pass = Console.ReadLine()
            let response = JsonConvert.SerializeObject(DS.Message("", "Login", JsonConvert.SerializeObject(Authorise(uname, pass)))) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif isNull userName || isNull password then
            Console.WriteLine("Please log in first")
            promptUserAction (userName, password)
        elif ip = "3" then
            Console.WriteLine ("Enter tweet")
            let tweet = Console.ReadLine()
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "MakeTweet", tweet)) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "4" then
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "SearchMyMentions", "")) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "5" then
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "SearchTweetsByFollowing", "")) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "6" then
            Console.WriteLine("Enter user to follow")
            let follow = Console.ReadLine()
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "FollowRequest", follow)) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "7" then
            Console.WriteLine("Enter #tag to search for")
            let tag = Console.ReadLine()
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "SearchTweetByTag", tag)) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        elif ip = "8" then
            let response = JsonConvert.SerializeObject(DS.Message(JsonConvert.SerializeObject(Authorise(userName, password)), "Logout", "")) |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>
            socket.SendAsync(response, WebSocketMessageType.Text, true, cts.Token).Wait()
        else
            Console.WriteLine("Invalid operation")
            promptUserAction (userName, password)

    let rec loop (userName: string, password: string) = actor {
        let! msg = mailbox.Receive()
        match box msg with
        | :? string as m ->
            let received = JsonConvert.DeserializeObject<DS.Message>(m)
            if received.Operation = "init" then
                promptUserAction(userName, password)
            if received.Operation = "LoginSuccess" then
                Console.WriteLine("Login success")
                let user = JsonConvert.DeserializeObject<Authorise>(received.Payload)
                promptUserAction (user.UserName, user.Password)
                return! loop(user.UserName, user.Password)
            elif received.Operation = "LoginFailed" then
                Console.WriteLine("Invalid username/password. Please try again.")
                promptUserAction (userName, password)
            elif received.Operation = "INFO" then
                Console.WriteLine("{0}", received.Payload)
                promptUserAction (userName, password)
            elif received.Operation = "DisplayTweets" then
                Console.WriteLine("{0}", received.Payload)
                promptUserAction (userName, password)
        | _ ->
            Console.WriteLine("Invalid message at client actor")
        return! loop(userName, password)
    }
    loop(null, null)

let main =
    let clientActorRef = spawn system "client_actor" clientActorFunction
    clientActorRef <! JsonConvert.SerializeObject (DS.Message ("", "init", ""))
    receiveMessageFromServer () |> ignore

main