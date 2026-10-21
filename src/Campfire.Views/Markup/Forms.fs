// Rails' form helpers as Falco.Markup functions (plans/design-views.md V13). First part: what `messages/**` needs. No form token is
// written (forgery protection is by `Sec-Fetch-Site`, `Campfire.App.Web.Csrf`).
namespace Campfire.Views.Markup

open Falco.Markup

module Forms =
    /// The hidden `_method` field (`method_tag`) of a form whose method is not get or post.
    let private methodTag (method: string) : XmlNode list =
        match method with
        | "get"
        | "post"
        | "" -> []
        | _ -> [ Elem.input [ Attr.create "type" "hidden"; Attr.create "name" "_method"; Html.attr "value" method ] ]

    let private methodAttr (method: string) : XmlAttribute =
        if method = "get" then Attr.create "method" "get" else Attr.create "method" "post"

    /// `form_with(url:, method:, id:, data:)` around `content`: the options, then `action`, `accept-charset` and `method`, and the
    /// `_method` field first inside.
    let formWith (url: string) (method: string) (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.form
            (options @ [ Html.attr "action" url; Attr.create "accept-charset" "UTF-8"; methodAttr method ])
            (methodTag method @ content)

    /// `hidden_field_tag(name, value)`.
    let hiddenFieldTag (name: string) (value: string) : XmlNode =
        Elem.input
            [ Attr.create "type" "hidden"
              Html.attr "name" name
              Html.attr "id" (Campfire.Views.Helpers.Forms.sanitizeToId name)
              Html.attr "value" value ]

    /// `button_tag(options) do ... end` / `form.button`: `name="button"` and `type="submit"` first, then the options.
    let buttonTag (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.button ([ Attr.create "name" "button"; Attr.create "type" "submit" ] @ options) content

    /// `button_to(url, options) do ... end` (`method`: "delete", "put", "patch", "post" or "get").
    let buttonTo (url: string) (method: string) (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.form
            [ Attr.class' "button_to"; methodAttr method; Html.attr "action" url ]
            (methodTag method @ [ Elem.button (options @ [ Attr.create "type" "submit" ]) content ])

    /// `Tags::Base#tag_name` / `tag_id` for a model-scoped field.
    let private nameAndId (objectName: string) (method: string) (idOverride: string option) : XmlAttribute list =
        [ Html.attr "name" (objectName + "[" + method + "]")
          Html.attr "id" (defaultArg idOverride (Campfire.Views.Helpers.Forms.sanitizeObjectName objectName + "_" + method)) ]

    /// `Tags::TextField#render`: the options in order, then `type`, `value`, `name` and `id`. A `maxlength` is written with its own
    /// `size` by the caller, as Rails does.
    let private inputField
        (fieldType: string)
        (objectName: string)
        (method: string)
        (idOverride: string option)
        (value: string option)
        (options: XmlAttribute list)
        : XmlNode =
        Elem.input (
            options
            @ [ Html.attr "type" fieldType
                match value with
                | Some value -> Html.attr "value" value
                | None -> () ]
            @ nameAndId objectName method idOverride
        )

    /// `form.text_field(method, options)`.
    let textField (objectName: string) (method: string) (value: string option) (options: XmlAttribute list) : XmlNode =
        inputField "text" objectName method None value options

    /// `form.email_field(method, options)`.
    let emailField (objectName: string) (method: string) (value: string option) (options: XmlAttribute list) : XmlNode =
        inputField "email" objectName method None value options

    /// `form.password_field(method, options)`: never writes the model's value.
    let passwordField (objectName: string) (method: string) (options: XmlAttribute list) : XmlNode =
        inputField "password" objectName method None None options

    /// `form.file_field(method, options)`; the form that holds it needs `enctype="multipart/form-data"` (a form option).
    let fileField (objectName: string) (method: string) (idOverride: string option) (options: XmlAttribute list) : XmlNode =
        inputField "file" objectName method idOverride None options

    /// `form.text_area(method, options)`: Rails writes a newline right after the opening tag.
    let textArea (objectName: string) (method: string) (value: string option) (options: XmlAttribute list) : XmlNode =
        Elem.textarea
            (options @ nameAndId objectName method None)
            [ Html.nl
              match value with
              | Some value -> Html.text value
              | None -> () ]
