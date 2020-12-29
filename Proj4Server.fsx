#load "bootstrap.fsx"
#load "DS.fsx"

open System
open System.Text
open Akka.FSharp
open Akka.Actor
open Akka.Configuration
open Newtonsoft.Json
open DS

open Suave
open Suave.Operators
open Suave.Filters
open Suave.RequestErrors
open Suave.Logging
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

// let system = ActorSystem.Create("FSServer", config)
let system = System.create "FSServer" <| ConfigurationFactory.Default()

let mutable user_db: Map<string, User> = Map.empty

let mutable tweets_db: Map<User, Tweet list> = Map.empty
let mutable hashTagMap: Map<string, Tweet list> = Map.empty
let mutable mentionsMap: Map<string, Tweet list> = Map.empty

let mutable liveUsersMap: Map<User, WebSocket> = Map.empty

type TweetMessages =
| MakeTweet of User * string * int64

type MessageToServerActor =
| MessageToServerActor of string * Option<WebSocket>

let serverActorFunction (mailbox: Actor<_>) =

    let validateUser (user: Authorise) =
        match user_db.TryFind user.UserName with
        | Some x ->
            if x.Password = user.Password then
                x
            else
                null
        | None ->
            null

    let addLiveUser (user: User) =
        0

    let registerMaps (tweet: Tweet) =
        for i = 0 to (List.length tweet.HashTags)-1 do
            match hashTagMap.TryFind tweet.HashTags.[i] with
            | Some x ->
                let newTweetList = List.append x [tweet]
                hashTagMap <- hashTagMap.Add (tweet.HashTags.[i], newTweetList)
            | None ->
                hashTagMap <- hashTagMap.Add (tweet.HashTags.[i], [tweet])
        for i = 0 to (List.length tweet.Mentions)-1 do
            match mentionsMap.TryFind tweet.Mentions.[i] with
            | Some x ->
                let newTweetList = List.append x [tweet]
                mentionsMap <- mentionsMap.Add (tweet.Mentions.[i], newTweetList)
            | None ->
                mentionsMap <- mentionsMap.Add (tweet.Mentions.[i], [tweet])

    let registerTweet (author: User, tweetMessage: string) =
        let newTweet = Tweet(author.UserName, tweetMessage)
        match tweets_db.TryFind author with
        | Some x ->
            let newTweetList = List.append x [newTweet]
            tweets_db <- tweets_db.Add (author, newTweetList)
        | None ->
            tweets_db <- tweets_db.Add (author, [newTweet])
        registerMaps (newTweet)

    let searchMentionedTweets (userName: string) =
        match mentionsMap.TryFind userName with
        | Some x ->
            x
        | None ->
            List.empty

    let getUser (user: string) =
        match user_db.TryFind user with
        | Some x ->
            x
        | None ->
            null

    let registerUser (user: User, client: Option<WebSocket>) =
        if isNull(getUser(user.UserName)) then
            user_db <- user_db.Add (user.UserName, user)
            liveUsersMap <- liveUsersMap.Add (user, client.Value)
            Console.WriteLine ("{0} has registered", user.UserName)
            JsonConvert.SerializeObject(DS.Message("", "LoginSuccess", JsonConvert.SerializeObject(Authorise(user.UserName, user.Password)), ""))
        else
            JsonConvert.SerializeObject(DS.Message("", "INFO", "Username already exists", ""))

    let searchMentionedTweets (userName: string) =
        match mentionsMap.TryFind userName with
        | Some x ->
            x
        | None ->
            List.empty

    let follow (user:User, userName: string) =
        let user2 = getUser(userName)
        if not(isNull(user2)) then
            user.AddFollowing(user2)
            user2.AddFollower(user)
            true
        else 
            false

    let searchTweetsByFollowing (user: User) =
        let mutable tweetList: Tweet list = List.Empty
        for i = 0 to (List.length (user.GetFollowing()))-1 do
            match tweets_db.TryFind (user.GetFollowing().[i]) with
            | Some x ->
                tweetList <- List.append tweetList x
            | None ->
                0 |> ignore
        tweetList

    let searchTweetWithTag (tag: string) =
        match hashTagMap.TryFind tag with
        | Some x ->
            x
        | None ->
            List.empty

    let rec loop () = actor {
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        match msg with
        | MessageToServerActor (mm, client) ->
            let request = JsonConvert.DeserializeObject<DS.Message>(mm)
            if request.Operation = "init" then
                Console.WriteLine ("Initializing server")
            elif request.Operation = "Register" then
                let payload = JsonConvert.DeserializeObject<DS.Authorise>(request.Payload)
                let newUser = User(payload.UserName, payload.Password)
                sender <! registerUser (newUser, client)
            elif request.Operation = "Login" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.Payload)
                let user = validateUser(authorName)
                if isNull(user) then
                    sender <! JsonConvert.SerializeObject(DS.Message("", "LoginFailed", "", ""))
                else
                    Console.WriteLine("{0} logged in", user.UserName)
                    sender <! JsonConvert.SerializeObject(DS.Message("", "LoginSuccess", JsonConvert.SerializeObject(Authorise(user.UserName, user.Password)), ""))
            elif request.User = "" then
                sender <! JsonConvert.SerializeObject(DS.Message("", "LoginFailed", "", ""))
            elif request.Operation = "MakeTweet" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    registerTweet (author, request.Payload)
                    let response = JsonConvert.SerializeObject(DS.Message(request.User, "INFO", "Tweet registered successfully"))
                    sender <! response
                else
                    sender <! JsonConvert.SerializeObject(DS.Message("", "INFO", "Tweet was not registered"))
            elif request.Operation = "SearchMyMentions" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    let mentionedTweets = JsonConvert.SerializeObject (searchMentionedTweets (authorName.UserName))
                    let response = JsonConvert.SerializeObject(DS.Message(request.User, "DisplayTweets", mentionedTweets))
                    sender <! response
            elif request.Operation = "SearchTweetsByFollowing" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    let response = JsonConvert.SerializeObject(DS.Message(request.User, "DisplayTweets", JsonConvert.SerializeObject(searchTweetsByFollowing(author))))
                    sender <! response
            elif request.Operation = "FollowRequest" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    if follow (author, request.Payload) then
                        sender <! JsonConvert.SerializeObject(DS.Message("", "INFO", (sprintf "Following %s" request.Payload)))
                    else
                        sender <! JsonConvert.SerializeObject(DS.Message("", "INFO", (sprintf "%s does not exist" request.Payload)))
            elif request.Operation = "SearchTweetByTag" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    let response = JsonConvert.SerializeObject(DS.Message(request.User, "DisplayTweets", JsonConvert.SerializeObject(searchTweetWithTag(request.Payload))))
                    sender <! response
            elif request.Operation = "Retweet" then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    registerTweet (author, (sprintf "Retweeted by %s: %s" author.UserName request.Payload))
                    let response = JsonConvert.SerializeObject(DS.Message(request.User, "INFO", "Retweet registered successfully"))
                    sender <! response
                else
                    sender <! JsonConvert.SerializeObject(DS.Message("", "INFO", "Retweet was not registered"))
            elif request.Operation = "Logout"then
                let authorName = JsonConvert.DeserializeObject<Authorise>(request.User)
                let author = validateUser (authorName)
                if not(isNull author) then
                    liveUsersMap <- liveUsersMap.Remove author
                Console.WriteLine("{0} has logged out", authorName.UserName)
                sender <! JsonConvert.SerializeObject(DS.Message("", "Logout", ""))
            else
                Console.WriteLine ("Operation: {0}", request.Operation)
                sender <! JsonConvert.SerializeObject(DS.Message("", "Error", "Unexpected request to server"))
        // | _ ->
        //     Console.WriteLine("Hello")
        return! loop()
    }
    loop()

let serverWS (webSocket: WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true
        while loop do
            let! msg = webSocket.read()
            match msg with
            | (Text, data, true) ->
                let str = UTF8.toString data
                let serverActorRef = system.ActorSelection ("akka://FSServer/user/server_actor")
                let callActor = async {
                    let! res = serverActorRef <? MessageToServerActor(str, Some webSocket)
                    return res
                }
                // let request = JsonConvert.DeserializeObject<DS.Message>(str)
                let reply = Async.RunSynchronously(callActor, 5000) |> string
                let replyMessage = JsonConvert.DeserializeObject<DS.Message>(reply)
                
                if replyMessage.Operation = "INFO" && (replyMessage.Payload = "Tweet registered successfully" || replyMessage.Payload = "Retweet registered successfully") then
                    let originalMsg = JsonConvert.DeserializeObject<DS.Message>(str)
                    let tweet = Tweet(JsonConvert.DeserializeObject<Authorise>(originalMsg.User).UserName, originalMsg.Payload)
                    let author = JsonConvert.DeserializeObject<Authorise>(originalMsg.User)
                    match user_db.TryFind author.UserName with
                    | Some x ->
                        // Console.WriteLine ("HERE {0}", (List.length (x.GetFollowers())))
                        for i in x.GetFollowers() do
                            if not(List.exists (fun mentioned -> mentioned = i.UserName) tweet.Mentions) then
                                match liveUsersMap.TryFind i with
                                | Some y ->
                                    do! y.send Text (JsonConvert.SerializeObject(DS.Message(author.UserName,
                                                                                    "TweetNotification", originalMsg.Payload)) |> Encoding.ASCII.GetBytes |> ByteSegment) true
                                | None ->
                                    0 |> ignore
                    | None ->
                        0 |> ignore

                    for i in tweet.Mentions do
                        match (liveUsersMap.TryFind (User(i, ""))) with
                        | Some x ->
                            0 |> ignore
                            do! x.send Text (JsonConvert.SerializeObject(DS.Message(author.UserName,
                                                                            "TweetNotification", originalMsg.Payload)) |> Encoding.ASCII.GetBytes |> ByteSegment) true
                        | None ->
                            0 |> ignore
                do! webSocket.send Text (reply |> Encoding.ASCII.GetBytes |> ArraySegment<Byte>) true
                // Console.WriteLine("{0}", reply)
            | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false
            | _ ->
                0 |> ignore
    }

let mainServerApp: WebPart =
    choose [
        path "/init" >=> handShake serverWS
        NOT_FOUND "No handlers"
    ]

let main =
    let serverActorRef = spawn system "server_actor" serverActorFunction
    serverActorRef <! MessageToServerActor(JsonConvert.SerializeObject (DS.Message ("", "init", "")), None)
    startWebServer { defaultConfig with
                      logger = Targets.create Verbose [||]
                      bindings = [HttpBinding.createSimple HTTP "127.0.0.1" 9001] } mainServerApp
    
main