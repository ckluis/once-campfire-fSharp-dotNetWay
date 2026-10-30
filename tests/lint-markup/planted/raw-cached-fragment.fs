// plant: raw-maker
let a (html: string) = Elem.div [] [ Html.fragment (CachedFragment html) ]
