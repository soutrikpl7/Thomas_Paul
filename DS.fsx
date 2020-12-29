[<AllowNullLiteralAttribute>]
type Authorise (userName: string, password: string) =
    member this.UserName = userName
    member this.Password = password


type Message (user: string, operation: string, payload: string, timeStamp: string) = class
    
    let mutable user = user
    let mutable op = operation
    let mutable pay = payload
    let mutable time = timeStamp

    member this.Operation
        with set v =
            op <- v
        and get () =
            op
    member this.Payload
        with set v =
            pay <- v
        and get () =
            pay
    member this.TimeStamp
        with set v =
            time <- v
        and get () =
            time
    member this.User
        with set v =
            user <- v
         and get () =
            user

    new () =
        Message ("", "", "", "")

    new (author: string, op: string, pay: string) =
        Message (author, op, pay, "")

end


[<AllowNullLiteralAttribute>]
type User (userName: string, password: string) =

    let mutable followers: User list = List.Empty
    let mutable following: User list = List.Empty
    
    member this.UserName = userName
    member this.Password = password

    member this.GetFollowers() =
        followers
    
    member this.SetFollowers(newFollowers: User list) =
        followers <- newFollowers

    member this.AddFollower(userId) =
        followers <- List.append followers [userId]

    member this.GetFollowing() =
        following

    member this.SetFollowing(newFollowing: User list) =
        following <- newFollowing

    member this.AddFollowing(userId: User) =
        following <- List.append following [userId]

    interface System.IComparable<User> with
        member this.CompareTo(user: User) =
            if isNull user then
                1
            else
                System.String.Compare(this.UserName, user.UserName)

    interface System.IComparable with
        member this.CompareTo(user) =
            if isNull user then
                1
            else
                match user with
                | :? User as u ->
                    System.String.Compare(this.UserName, u.UserName)
                | _ ->
                    invalidArg "obj" "not a User"

    override this.Equals(user) =
        if isNull user then
            false
        else
            match user with
            | :? User as u ->
                this.UserName = u.UserName
            | _ ->
                invalidArg "obj" "not a User"

    override this.GetHashCode() =
        this.UserName.GetHashCode()


type Tweet(author: string, message: string) =

    member this.Author = author
    member this.Message = message

    member this.Mentions: string list = 
        let mutable mentionsList = List.Empty
        let mutable  i = 0
        while i < this.Message.Length do
            if this.Message.[i] = '@' then
                i <- i+1
                let mutable mention = ""
                while i < this.Message.Length && this.Message.[i] <> ' ' do
                    mention <- mention + string(this.Message.[i])
                    i <- i+1
                if mention.Length > 0 then
                    mentionsList <- List.append mentionsList [mention]
                i <- i-1
            i <- i+1
        mentionsList
        
    member this.HashTags: string list =
        let mutable hashTagsList = List.Empty
        let mutable  i = 0
        while i < this.Message.Length do
            if this.Message.[i] = '#' then
                i <- i+1
                let mutable hashTag = ""
                while i < this.Message.Length && this.Message.[i] <> ' ' do
                    hashTag <- hashTag + string(this.Message.[i])
                    i <- i+1
                if hashTag.Length > 0 then
                    hashTagsList <- List.append hashTagsList [hashTag]
                i <- i-1
            i <- i+1
        hashTagsList