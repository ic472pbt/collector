namespace Common

module Channel =
    type QueueCommand =
        | Push of svdmpDTO.svdmpDTO*AsyncReplyChannel<bool>
        | Pop of AsyncReplyChannel<svdmpDTO.svdmpDTO>
    
    type Bridge() = 
        let bridge = MailboxProcessor<QueueCommand>.Start(fun inbox ->
            let rec innerLoop (mem:(svdmpDTO.svdmpDTO * AsyncReplyChannel<bool>) option, poper: AsyncReplyChannel<svdmpDTO.svdmpDTO> option) = async{
                match! inbox.Receive() with
                | Pop rc -> 
                    match mem, poper with
                    | Some m, _ -> rc.Reply (fst m); (snd m).Reply(true); return! innerLoop (None, None)
                    | None, _ -> return! innerLoop (None, Some rc)
                | Push (dto, rc) ->
                    match mem, poper with
                    | _, Some p -> p.Reply(dto); rc.Reply(true); return! innerLoop (None, None)
                    | _, None -> return! innerLoop (Some (dto, rc), None)
            }
            innerLoop (None, None)
        )
        member _.Pop() = bridge.PostAndAsyncReply (fun rc -> Pop(rc)) |> Async.StartAsTask
        member _.Push(data) = bridge.PostAndAsyncReply (fun rc -> Push(data,rc))

    let bridgeInstance = Bridge()

