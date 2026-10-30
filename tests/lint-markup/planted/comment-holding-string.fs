// plant: text-literal
(* F# lexes strings inside block comments, so "*)" does not end this one: the comment ends at the last *)
let a body = Elem.div [] [ Text.raw body ]
// "
