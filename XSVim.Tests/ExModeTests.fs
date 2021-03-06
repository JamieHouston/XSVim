﻿namespace XSVim.Tests
open NUnit.Framework
open XSVim

[<TestFixture>]
module ``Ex mode tests`` =
    [<Test>]
    let ``/ searches for word``() =
        assertText "ab$c abc" "/abc<ret>" "abc a$bc"

    [<Test>]
    let ``/ is case insensitive``() =
        assertText "ab$c ABC" "/abc<ret>" "abc A$BC"

    [<Test>]
    let ``/ is case sensitive``() =
        assertText "ab$c ABC Abc" "/Abc<ret>" "abc ABC A$bc"

    [<Test>]
    let ``deletes to search term``() =
        assertText "ab$c ABC Abc 123" "d/123<ret>" "a1$23"

    [<Test>]
    let ``n searches for next word``() =
        assertText "ab$c abc abc" "/abc<ret>n" "abc abc a$bc"

    [<Test>]
    let ``n wraps to start``() =
        assertText "ab$c abc abc" "/abc<ret>nn" "a$bc abc abc"

    [<Test>]
    let ``N searches for previous word``() =
        assertText "ab$c abc abc" "/abc<ret>N" "a$bc abc abc"

    [<Test>]
    let ``n searches for previous word after ?``() =
        assertText "abc abc a$bc" "?abc<ret>n" "a$bc abc abc"

    [<Test>]
    let ``? searches for word backwards``() =
        assertText "abc abc a$bc" "?abc<ret>" "abc a$bc abc"

    [<Test>]
    let ``Backspacing ex mode returns to normal mode``() =
        let _, state = test "abc abc a$bc" "/a<bs><bs>"
        state.mode |> should equal NormalMode