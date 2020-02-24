﻿module Enumerate

open SlicStanSyntax
open Elaborate
open Shredding
open Typecheck
open Util

let mutable message_number = 0
let next_message () = 
    message_number <- message_number + 1
    sprintf "m%A" message_number

let is_discrete_parameter W = 
    fun (x, (tp, tl)) -> tl = Model && tp <. Int && (Set.contains x W |> not)

let discrete_vars W gamma = 
    Map.toList gamma 
    |> List.filter (is_discrete_parameter W) 
    |> List.map fst 
    |> Set.ofList

let discrete_vars_S dvs s =
    Set.intersect (reads s ) dvs

let can_reorder s1 s2 = 
    (Set.intersect (assigns s1) (assigns s2) |> Set.isEmpty) &&
    (Set.intersect (reads s1) (assigns s2) |> Set.isEmpty) &&
    (Set.intersect (assigns s1) (reads s2) |> Set.isEmpty)
    
let rec filter_Skips s =
    match s with
    | Seq(Skip, s') -> filter_Skips s'
    | Seq(s', Skip) -> filter_Skips s'
    | Seq(s1, s2) -> Seq(filter_Skips s1, filter_Skips s2)
    | Message(arg, args, s') -> Message(arg, args, filter_Skips s') 
    | Elim(arg, s') -> Elim(arg, filter_Skips s') 
    | Generate(arg, s') -> Generate(arg, filter_Skips s') 
    | _ -> s
    

let rec all_dependent_transformed_vars (s : S) (vars : Set<Ide>) : Set<Ide> =
    let rec single_pass (s : S) : Set<Ide> = 
        match s with 
        | Assign(L, E) -> 
            let dependent = if Set.intersect vars (read_exp E) |> Set.isEmpty
                            then Set.empty
                            else lhs_to_exp L |> read_exp
            let depends_on = if Set.intersect vars (lhs_to_exp L |> read_exp) |> Set.isEmpty
                             then Set.empty
                             else read_exp E 

            //Set.union dependent depends_on
            dependent

        | Seq(s1, s2) -> Set.union (single_pass s1) (single_pass s2)
        | Message(_, _, s') -> single_pass s'
        | Elim(_, s') -> single_pass s'
        | If(_, s1, s2) -> Set.union (single_pass s1) (single_pass s2)
        | For(_, _, _, s') ->  single_pass s'
        | _ -> Set.empty

    let new_vars = single_pass s
    let new_result = Set.union vars new_vars

    if vars = new_result then vars
    else all_dependent_transformed_vars s (new_result)

let rec all_dependent_vars (s : S) (vars : Set<Ide>) : Set<Ide> = 
    let rec inner S =
        match S with 
        | Seq(s1, s2) -> Set.union (inner s1) (inner s2)
        | Generate _ -> Set.empty
        | _ -> 
            if Set.intersect (reads S) vars |> Set.isEmpty then Set.empty
            else (reads S)
    inner s 

let neighbour v1 v2 s = 
    let x1 = all_dependent_transformed_vars s (Set.add v2 (Set.empty))
    let x1' = all_dependent_transformed_vars s (Set.add v1 (Set.empty))
    let x2 = x1 |> all_dependent_vars s 
    let x2' = x1' |> all_dependent_vars s 
    let x3 = x2 |> Set.contains v1 && v1 <> v2
    let x3' = x2' |> Set.contains v2 && v1 <> v2
    x3 || x3'
    

let enum (gamma : Gamma, s : S) (d: Ide) : Gamma * S = 
    
    let sd, sm, sq = Shredding.shred_S gamma s 

    //printfn "Gamma temp: %A\n\n" gamma_temp
    // printfn "\n***FIRST shredding: SD: %A\n\nSM: %A\n\nSQ: %A" (S_pretty "" sd) (S_pretty "" sm) (S_pretty "" sq) 
   
    let W = assigns sm


    let neighbours = gamma
                   |> Map.filter (fun x (_, level) -> 
                        match level with 
                        | Model -> Set.contains x W |> not && neighbour x d s
                        | _ -> false)
                   |> Map.filter (fun x (tau, _) -> 
                        match tau with 
                        | Constrained(Int, _) -> true
                        | _ -> false)
                   |> Map.toList
                   |> List.map fst 
                   //|> Set.ofList

    let gamma_partial = 
        Map.map (fun x (tau, level) -> 
                    match level with
                    | Model ->  if tau <. Int && (Set.contains x W |> not) then //tau, Model 
                                    if x = d then tau, Model
                                    elif Set.contains x (Set.ofList neighbours) then tau, Model
                                    else tau, GenQuant
                                elif Real <. tau && (Set.contains x W |> not) then tau, Data                                
                                else tau, LevelVar(SlicStanSyntax.next()) 
                    | _ -> tau, level
                                
                ) gamma
    
    Typecheck.toplevel <- false
    Typecheck.dv <- d

    let gamma_temp, sm' = Typecheck.typecheck_elaborated gamma_partial sm

    let sm1, sm2, sm22 = Shredding.shred_S gamma_temp sm'

    // printfn "\n***SECOND shredding: SD: %A\n\nSM: %A\n\nSQ: %A" (S_pretty "" sm1) (S_pretty "" sm2) (S_pretty "" sm22) 


    let mutable message_name = next_message() 
    while Map.containsKey message_name gamma do
        message_name <- next_message()
    
    let tau_list = List.map (fun ne -> Map.find ne gamma |> fst) neighbours
    let tau_sizes = List.map (fun t ->
                        match t with 
                        | Constrained (Int, K) -> K 
                        | _ -> failwith "Discrete parameters must have specified support") tau_list

    let tau = List.fold (fun s t -> Array(s, t)) Int tau_sizes 
    let arg = (tau, Model), message_name

    let tau_d = Map.find d gamma |> fst, Data

    let gamma' = gamma
                |> Map.add d (Int, GenQuant) 
                |> Map.add message_name (tau, Model)

    let s_message = Message ( arg, neighbours, Elim( (tau_d, d), sm2 ) )
    let s_factor = Factor ( Util.indices_list_to_exp (Var(message_name)) (List.map Var neighbours) )
    let s_gen = Generate ( ((Int, GenQuant), d), sm2)

    let s' = SlicStanSyntax.SofList [ sd; sm1; s_message; s_factor; sm22; s_gen; sq ]
    
    Typecheck.toplevel <- true
    gamma', (filter_Skips s')


