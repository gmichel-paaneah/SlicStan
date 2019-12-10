﻿// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

open SlicStanSyntax

open FSharp.Text.Lexing
open Parser
open Lexer 

open Typecheck
open Elaborate
open Enumerate
open Shredding
open Transformation

open Constraints

open System.Diagnostics;

open System.IO
open Factorgraph


let parse slicstan = 
    let lexbuf = LexBuffer<char>.FromString slicstan
    let res = Parser.start Lexer.read lexbuf
    res    


// d1 -> d2 -> d3 <- d4 <- d5

let example = Examples.discrete_statement_reordering
let name = Util.get_var_name <@Examples.discrete_statement_reordering@>
printfn "Name is %s" name
set_folder (name)

// enumerate only ints of level models that are not TP

[<EntryPoint>]
let main argv =   

    let option = try argv.[0] with _ -> "--no-input"
    
    let slic = match option with 
               | "--fromfile" -> 
                    let reader = File.OpenText(argv.[1])
                    reader.ReadToEnd()

               | "--no-input" -> example

               | _ -> option

    printfn "%s\n\n" slic 
    
    (*  BOOKMARK: ToDo next
        * The example `discrete_statement_reordering` is not resolved in the 
          most efficient way at the moment. See if there is a straightforward 
          way to fix it. If not, maybe leave for later: it's still correct. 
        
        ----------------------------------------------------------------------
        * Think about discrete variable arrays. It will be a shame if we don't 
          implemnt this. Yes, we can unroll the loops for loop bounds known 
          at static time, but really I think we can do better. 

        * A natural step is to implemented the junction tree algorithm, so 
          that loops between discrete variables can also be implemented. But
          is more of an extra; shouldn't be needed for a paper?    
    *)

    let typechecked = slic
                    |> parse
                    |> typecheck_Prog 


    let W, graph = Factorgraph.to_graph (snd typechecked)
    graphviz graph 0 "init"
    let ordering = Factorgraph.find_ordering W graph //|> List.rev
    //let ordering = ["d1"; "d2"; "d3"; "d4"; "d5"]
    printfn "Ordering:\n%A" ordering

    let elaborated =  elaborate_Prog typechecked
    
    let gamma, enum = 
        List.fold (Enumerate.enum) elaborated ordering
    
    printfn "\n\nSlicStan reduced:\n%A" (SlicStanSyntax.S_pretty "" enum)

    //let gamma, s = elaborated

    let sd, sm, sq = shred_S gamma enum

    let stan = transform gamma (sd, sm, sq)
    
    printfn "%s" (MiniStanSyntax.Prog_pretty stan)      

    0



