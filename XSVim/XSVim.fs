﻿namespace XSVim

open System
open MonoDevelop.Ide.Editor
open MonoDevelop.Ide.Editor.Extension
open Mono.TextEditor
open MonoDevelop.Core

type VimMode =
    | NormalMode
    | VisualMode
    | InsertMode

type CommandType =
    | Move
    | Visual
    | Delete
    | Change
    | Go
    | SwitchMode of VimMode
    | Undo
    | InsertLineAbove
    | InsertLineBelow
    | RepeatLastAction
    | RepeatLastFindCommand
    | ResetKeys
    | DoNothing

type TextObject =
    | Character
    | AWord
    | InnerWord
    | AWORD
    | InnerWORD
    | ASentence
    | InnerSentence
    | AParagraph
    | InnerParagraph
    | ABlock of string * string
    | InnerBlock of string * string
    | WholeLine
    | LastLine
    // motions
    | Up
    | Down
    | Left
    | Right
    | FirstNonWhitespace
    | StartOfLine
    | StartOfDocument
    | EndOfLine
    | ToCharInclusive of string
    | ToCharInclusiveBackwards of string
    | ToCharExclusive of string
    | ToCharExclusiveBackwards of string
    | WordForwards
    | WORDForwards
    | WordBackwards
    | WORDBackwards
    | ForwardToEndOfWord
    | ForwardToEndOfWORD
    | BackwardToEndOfWord
    | BackwardToEndOfWORD
    | Nothing
    | HalfPageUp
    | HalfPageDown

type VimAction = {
    repeat: int
    commandType: CommandType
    textObject: TextObject
}

type VimState = {
    keys: string list
    mode: VimMode
    desiredColumn: int // If we move up/down, which column did we start at?
    findCharCommand: VimAction option
    lastAction: VimAction list // used by . command to repeat the last action
}

module VimHelpers =
    let findCharForwardsOnLine (editor:TextEditorData) (line:DocumentLine) character =
        let ch = Char.Parse character
        seq { editor.Caret.Offset+1 .. line.EndOffset }
        |> Seq.tryFind(fun index -> editor.Text.[index] = ch)

    let findCharBackwardsOnLine (editor:TextEditorData) (line:DocumentLine) character =
        let ch = Char.Parse character
        seq { editor.Caret.Offset-1 .. -1 .. line.Offset }
        |> Seq.tryFind(fun index -> editor.Text.[index] = ch)

    let findCharForwards (editor:TextEditorData) character =
        let ch = Char.Parse character
        seq { editor.Caret.Offset+1 .. editor.Text.Length }
        |> Seq.tryFind(fun index -> editor.Text.[index] = ch)

    let findCharBackwards (editor:TextEditorData) character =
        let ch = Char.Parse character
        seq { editor.Caret.Offset .. -1 .. 0 }
        |> Seq.tryFind(fun index -> editor.Text.[index] = ch)

    let findCharRange (editor:TextEditorData) startChar endChar =
        findCharBackwards editor startChar, findCharForwards editor endChar

    let getVisibleLineCount (editor:TextEditorData) =
        let topVisibleLine = ((editor.VAdjustment.Value / editor.LineHeight) |> int) + 1
        let bottomVisibleLine =
            Math.Min(editor.LineCount - 1,
                topVisibleLine + ((editor.VAdjustment.PageSize / editor.LineHeight) |> int))
        bottomVisibleLine - topVisibleLine

    let getRange (editor:TextEditorData) motion =
        let line = editor.GetLine editor.Caret.Line
        match motion with
        | Right -> 
            let line = editor.GetLine editor.Caret.Line
            editor.Caret.Offset, if editor.Caret.Column < line.Length then editor.Caret.Offset + 1 else editor.Caret.Offset
        | Left -> editor.Caret.Offset, if editor.Caret.Column > DocumentLocation.MinColumn then editor.Caret.Offset - 1 else editor.Caret.Offset
        | Up ->
            editor.Caret.Offset,
            if editor.Caret.Line > DocumentLocation.MinLine then
                let visualLine = editor.LogicalToVisualLine(editor.Caret.Line)
                let lineNumber = editor.VisualToLogicalLine(visualLine - 1)
                editor.LocationToOffset (new DocumentLocation(lineNumber, editor.Caret.Column))
            else
                editor.Caret.Offset
        | Down ->
            editor.Caret.Offset,
            if editor.Caret.Line < editor.Document.LineCount then
                let visualLine = editor.LogicalToVisualLine(editor.Caret.Line)
                let lineNumber = editor.VisualToLogicalLine(visualLine + 1)
                editor.LocationToOffset (new DocumentLocation(lineNumber, editor.Caret.Column))
            else
                editor.Caret.Offset
        | EndOfLine -> editor.Caret.Offset, line.EndOffset
        | StartOfLine -> editor.Caret.Offset, line.Offset
        | StartOfDocument -> editor.Caret.Offset, 0
        | FirstNonWhitespace -> editor.Caret.Offset, line.Offset + editor.GetLineIndent(editor.Caret.Line).Length
        | WholeLine -> line.Offset, line.EndOffsetIncludingDelimiter
        | LastLine -> 
            let lastLine = editor.GetLine editor.Document.LineCount
            editor.Caret.Offset, lastLine.Offset
        | ToCharInclusiveBackwards c ->
            match findCharBackwardsOnLine editor line c with
            | Some index -> editor.Caret.Offset, index
            | None -> editor.Caret.Offset, editor.Caret.Offset
        | ToCharInclusive c ->
            match findCharForwardsOnLine editor line c with
            | Some index -> editor.Caret.Offset, index
            | None -> editor.Caret.Offset, editor.Caret.Offset
        | ToCharExclusive c ->
            match findCharForwardsOnLine editor line c with
            | Some index -> editor.Caret.Offset, index
            | None -> editor.Caret.Offset, editor.Caret.Offset
        | InnerBlock (startChar, endChar) ->
            match findCharRange editor startChar endChar with
            | Some start, Some finish -> start+1, finish
            | _, _ -> editor.Caret.Offset, editor.Caret.Offset
        | ABlock (startChar, endChar) ->
            match findCharRange editor startChar endChar with
            | Some start, Some finish when finish < editor.Text.Length -> start, finish+1
            | _, _ -> editor.Caret.Offset, editor.Caret.Offset
        | WordForwards -> editor.Caret.Offset, editor.FindNextWordOffset (editor.Caret.Offset) + 1
        | WordBackwards -> editor.Caret.Offset, editor.FindPrevWordOffset editor.Caret.Offset
        | ForwardToEndOfWord ->
            let endOfWord = editor.FindCurrentWordEnd (editor.Caret.Offset+1) - 1
            let endOfWord = 
                if editor.Text.[endOfWord] = ' ' then editor.FindCurrentWordEnd (endOfWord+1) - 1 else endOfWord
            editor.Caret.Offset, endOfWord
        | BackwardToEndOfWord -> editor.Caret.Offset, editor.FindPrevWordOffset editor.Caret.Offset |> editor.FindCurrentWordEnd
        | HalfPageUp -> 
            let visibleLineCount = getVisibleLineCount editor
            let halfwayUp = Math.Max(1, editor.Caret.Line - visibleLineCount / 2)
            editor.Caret.Offset, editor.GetLine(halfwayUp).Offset
        | HalfPageDown -> 
            let visibleLineCount = getVisibleLineCount editor
            let halfwayDown = Math.Min(editor.Document.LineCount, editor.Caret.Line + visibleLineCount / 2)
            editor.Caret.Offset, editor.GetLine(halfwayDown).Offset
        | _ -> 0,0

type XSVim() =
    inherit TextEditorExtension()
    let runCommand vimState textEditorData command =
        for i in [1..command.repeat] do
            let start, finish = VimHelpers.getRange textEditorData command.textObject
            match command.commandType with
            | Move -> textEditorData.Caret.Offset <- finish
            | Delete ->
                textEditorData.SetSelection(start, finish)
                ClipboardActions.Cut textEditorData
            | Visual -> textEditorData.SetSelection(start, finish)
            | Undo -> MiscActions.Undo textEditorData
            | InsertLineBelow -> MiscActions.InsertNewLineAtEnd textEditorData
            | InsertLineAbove -> textEditorData.Caret.Column <- 1; MiscActions.InsertNewLine textEditorData; CaretMoveActions.Up textEditorData
            | _ -> ()

        match command.commandType with
        | ResetKeys -> { vimState with keys = [] }
        | SwitchMode mode ->
            match mode with
            | NormalMode -> 
                textEditorData.Caret.Mode <- CaretMode.Block
                textEditorData.Caret.PreserveSelection <- false
                { vimState with mode = mode }
            | VisualMode -> 
                textEditorData.Caret.Mode <- CaretMode.Block
                textEditorData.Caret.PreserveSelection <- true
                { vimState with mode = mode }
            | InsertMode ->
                textEditorData.Caret.Mode <- CaretMode.Insert
                textEditorData.Caret.PreserveSelection <- false
                { vimState with mode = mode; keys = [] }
        | _ -> vimState

    let (|Digit|_|) character =
        if character > "0" && character < "9" then
            Some (Convert.ToInt32 character)
        else
            None

    let (|OneToNine|_|) character =
        if character > "1" && character < "9" then
            Some (Convert.ToInt32 character)
        else
            None

    let (|BlockDelimiter|_|) character =
        let pairs =
            [ 
                "[", ("[", "]")
                "]", ("[", "]")
                "(", ("(", ")")
                ")", ("(", ")")
                "{", ("{", "}")
                "}", ("{", "}")
                "<", ("<", ">")
                ">", ("<", ">")
            ] |> dict
        if pairs.ContainsKey character then
            Some pairs.[character]
        else
            None

    let (|Movement|_|) character =
        match character with
        | "h" -> Some Left
        | "j" -> Some Down
        | "k" -> Some Up
        | "l" -> Some Right
        | "$" -> Some EndOfLine
        | "^" -> Some StartOfLine
        | "0" -> Some StartOfLine
        | "_" -> Some FirstNonWhitespace
        | "w" -> Some WordForwards
        | "b" -> Some WordBackwards
        | "e" -> Some ForwardToEndOfWord
        | "E" -> Some BackwardToEndOfWord
        | "G" -> Some LastLine
        | "<C-d>" -> Some HalfPageDown
        | "<C-u>" -> Some HalfPageUp
        | _ -> None

    let (|FindChar|_|) character =
        match character with
        | "f" -> Some ToCharInclusive
        | "F" -> Some ToCharInclusiveBackwards
        | "t" -> Some ToCharExclusive
        | "T" -> Some ToCharExclusiveBackwards
        | _ -> None

    let (|Action|_|) character =
        match character with
        | "d" -> Some Delete
        | "c" -> Some Change
        | "v" -> Some Visual
        | "g" -> Some Go
        | _ -> None

    let (|ModeChange|_|) character =
        match character with
        | "i" -> Some InsertMode
        | "v" -> Some VisualMode
        | _ -> None

    let (|Keys|_|) (keys:string) =
        keys |> Seq.map(fun c -> c |> string) |> List.ofSeq |> Some

    let getCommand (repeat: int option) commandType textObject =
        { repeat=(match repeat with | Some r -> r | None -> 1); commandType=commandType; textObject=textObject }

    let wait = [ getCommand (Some 1) DoNothing Nothing ]

    let parseKeys (state:VimState) =
        let keyList = state.keys// |> Seq.toList
        let multiplier, keyList =
            match keyList with
            | OneToNine d1 :: Digit d2 :: Digit d3 :: Digit d4 :: t -> Some(d1 * 1000 + d2 * 100 + d3 * 10 + d4), t
            | OneToNine d1 :: Digit d2 :: Digit d3 :: t -> Some (d1 * 100 + d2 * 10 + d3), t
            | OneToNine d1 :: Digit d2 :: t -> Some (d1 * 10 + d2), t
            | OneToNine d :: t -> Some d,t
            | _ -> None, keyList

        let run = getCommand multiplier
        LoggingService.LogDebug (sprintf "%A %A" state.mode keyList)
        let newState =
            match keyList with
            | [ FindChar m; c ] -> { state with findCharCommand = run Move ( m c ) |> Some }
            | _ -> state

        let action =
            match state.mode, keyList with
            | InsertMode, [ "<esc>" ] -> [ run (SwitchMode NormalMode) Nothing ]
            | VisualMode, [ "<esc>" ] -> [ run (SwitchMode NormalMode) Nothing ]
            | NormalMode, [ Movement m ] -> [ run Move m ]
            | VisualMode, [ Movement m ] -> [ run Move m ]
            | NormalMode, [ FindChar m; c ] -> [ run Move (m c) ]
            | NormalMode, [ ";" ] -> match state.findCharCommand with Some command -> [ command ] | None -> []
            | NormalMode, [ "c"; Movement m ] -> [ run Delete m; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ Action action; Movement m ] -> [ run action m ]
            | NormalMode, [ "u" ] -> [ run Undo Nothing ]
            | NormalMode, [ "d"; "d" ] -> [ run Delete WholeLine ]
            | NormalMode, [ "c"; "c" ] -> [ run Delete WholeLine; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ "C" ] -> [ run Delete EndOfLine; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ "D" ] -> [ run Delete EndOfLine ]
            | NormalMode, [ Action action; FindChar m; c ] -> [ run action (m c) ]
            | NormalMode, [ Action action; "i"; BlockDelimiter c ] -> [ run action (InnerBlock c) ]
            | NormalMode, [ ModeChange mode ] -> [ run (SwitchMode mode) Nothing ]
            | NormalMode, [ "a" ] -> [ run Move Right; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ "A" ] -> [ run Move EndOfLine; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ "o" ] -> [ run InsertLineBelow Nothing; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ "O" ] -> [ run InsertLineAbove Nothing; run (SwitchMode InsertMode) Nothing ]
            | NormalMode, [ Action action ] -> wait
            | NormalMode, [ "g"; "g" ] -> [ run Move StartOfDocument ]
            | NormalMode, [ "." ] -> [ run RepeatLastAction Nothing ]
            | NormalMode, [ "g" ] -> wait
            | NormalMode, [ ";" ] -> [ run RepeatLastFindCommand Nothing ]
            | _         , _ :: _ :: _ :: _ :: t -> [ run ResetKeys Nothing ]
            | _ -> []
        multiplier, action, newState

    let handleKeyPress state (keyPress:KeyDescriptor) editorData =
        let newKeys =
            match state.mode, keyPress.KeyChar with
            | NormalMode, c when keyPress.ModifierKeys = ModifierKeys.Control ->
                state.keys @ [sprintf "<C-%c>" c]
            | NormalMode, c when keyPress.KeyChar <> '\000' -> state.keys @ [c |> string]
            | VisualMode, c when keyPress.KeyChar <> '\000' -> state.keys @ [c |> string]
            | VisualMode, c | InsertMode, c ->
                match keyPress.SpecialKey with
                | SpecialKey.Escape -> state.keys @ ["<esc>"]
                | _ -> state.keys
            | _ -> state.keys
        let newState = { state with keys = newKeys }
        let multiplier, action, newState = parseKeys newState
        LoggingService.LogDebug (sprintf "%A %A" multiplier action)
        let rec performActions actions' state handled =
            match actions' with
            | [] -> state, handled
            | h::t ->
                if h.commandType <> DoNothing then
                    let newState = runCommand state editorData h
                    performActions t { newState with keys = [] } true
                else 
                    newState, true
        match multiplier, action with
        | _, [ a ] when a.commandType = RepeatLastAction ->
            performActions state.lastAction newState false
        | _, actions ->
            performActions actions { newState with lastAction = actions } false

    let mutable vimState = { keys=[]; mode=NormalMode; desiredColumn=0; findCharCommand=None; lastAction=[] }

    override x.Initialize() =
        let editorData = x.Editor.GetContent<ITextEditorDataProvider>().GetTextEditorData()
        editorData.Caret.Mode <- CaretMode.Block

    override x.KeyPress descriptor =
        let editorData = x.Editor.GetContent<ITextEditorDataProvider>().GetTextEditorData()
        let oldState = vimState

        let newState, handledKeyPress = handleKeyPress vimState descriptor editorData
        LoggingService.LogDebug (sprintf "%A %A" newState handledKeyPress)
        vimState <- newState
        match oldState.mode with
        | InsertMode -> base.KeyPress descriptor
        | _ -> false
