// Port of rust/crates/views/src/messages/presentation.rs, as Falco.Markup (plans/design-views.md V13)
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Views
open Campfire.Views.Messages
open Campfire.Views.MessagesSupport

/// `MessagesHelper#message_presentation` and `Messages::AttachmentPresentation`
/// (`reference/app/helpers/messages_helper.rb`, `reference/app/helpers/messages/attachment_presentation.rb`).
/// The rich text of a message is `Html.Trusted` (what the sanitizer made, `Trusted.ofSanitized` in the presenter).
module Messages =
    /// `Message::THUMBNAIL_MAX_WIDTH` / `THUMBNAIL_MAX_HEIGHT`.
    let private thumbnailMaxWidth = 1200L
    let private thumbnailMaxHeight = 800L

    /// `message_sound_presentation`: a play button followed by the sound's image or text.
    let private soundPresentation (sound: SoundView) : XmlNode =
        Elem.div
            [ Attr.class' "sound"
              Attr.create "data-controller" "sound"
              Attr.create "data-action" "messages:play->sound#play"
              Html.attr "data-sound-url-value" sound.Url ]
            [ Elem.button [ Attr.class' "btn btn--plain"; Attr.create "data-action" "sound#play" ] [ Text.raw "🔊" ]
              match sound.Image, sound.Text with
              | Some image, _ ->
                  Elem.img
                      [ Html.attrInt "width" (int64 image.Width)
                        Html.attrInt "height" (int64 image.Height)
                        Attr.class' "align--middle"
                        Html.attr "src" image.Src ]
              | None, Some text -> Html.text text
              | None, None -> Text.empty ]

    /// `preview_dimensions`: the metadata size, scaled down to fit the thumbnail bounds.
    let private previewDimensions (attachment: AttachmentView) : (RubyNumber * RubyNumber) option =
        match attachment.Width, attachment.Height with
        | Some width, Some height ->
            if width.ToF <= float thumbnailMaxWidth && height.ToF <= float thumbnailMaxHeight then
                Some(width, height)
            else
                let widthFactor = float thumbnailMaxWidth / width.ToF
                let heightFactor = float thumbnailMaxHeight / height.ToF
                let scale = min widthFactor heightFactor
                Some(Float(width.ToF * scale), Float(height.ToF * scale))
        | _ -> None

    let private inlineMediaDimensionConstraints (dimensions: (RubyNumber * RubyNumber) option) (content: XmlNode list) : XmlNode =
        match dimensions with
        | Some(width, height) ->
            let aspectRatio = Float(width.ToF / height.ToF)
            Elem.div
                [ Attr.class' "max-inline-size center flex overflow-clip"
                  Html.attr "style" ("width: " + width.Half.ToString() + "px; aspect-ratio: " + aspectRatio.ToString() + ";") ]
                content
        | None -> Elem.div [ Attr.class' "max-inline-size center overflow-clip" ] content

    let private videoPreview (attachment: AttachmentView) (posterUrl: string) : XmlNode =
        inlineMediaDimensionConstraints
            (previewDimensions attachment)
            [ Elem.video
                  [ Html.attr "src" attachment.BlobPath
                    Html.attr "poster" posterUrl
                    Attr.create "controls" "controls"
                    Attr.create "preload" "none"
                    Attr.create "width" "100%"
                    Attr.create "height" "100%"
                    Attr.class' "message__attachment" ]
                  [] ]

    let private lightboxedImagePreview (attachment: AttachmentView) (thumbUrl: string) : XmlNode =
        let dimensions = previewDimensions attachment
        let size =
            match dimensions with
            | Some(width, height) -> [ Html.attr "width" (width.ToString()); Html.attr "height" (height.ToString()) ]
            | None -> []
        inlineMediaDimensionConstraints
            dimensions
            [ Elem.a
                  [ Attr.class' "flex"
                    Attr.create "data-lightbox-target" "image"
                    Attr.create "data-action" "lightbox#open"
                    Html.attr "data-lightbox-url-value" attachment.DownloadPath
                    Html.attr "href" attachment.BlobPath ]
                  [ Elem.img (size @ [ Attr.class' "message__attachment"; Attr.create "loading" "lazy"; Html.attr "src" thumbUrl ]) ] ]

    /// `render_link`: file icon, name, download link and share button, with no whitespace between.
    let private fileLink (ctx: ViewContext) (attachment: AttachmentView) : XmlNode =
        Elem.div
            [ Attr.class' "flex-inline align-center gap-half" ]
            [ Assets.imageSized ctx "common-file-text.svg" 22 [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]
              Elem.span [] [ Html.text attachment.Filename ]
              Elem.a
                  [ Attr.class' "btn message__action-btn hide-in-ios-pwa"
                    Attr.create "style" "--width: auto;"
                    Html.attr "href" attachment.DownloadPath ]
                  [ Assets.imageSized ctx "download.svg" 20 [ Attr.create "aria-hidden" "true" ]
                    Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Download " + attachment.Filename) ] ]
              Elem.button
                  [ Attr.class' "btn message__action-btn"
                    Attr.create "style" "--width: auto;"
                    Attr.create "data-controller" "web-share"
                    Attr.create "data-action" "web-share#share"
                    Html.attr "data-web-share-files-value" attachment.DownloadPath ]
                  [ Assets.imageSized ctx "share.svg" 20 [ Attr.create "aria-hidden" "true" ]
                    Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Share " + attachment.Filename) ] ] ]

    /// `Messages::AttachmentPresentation#render`.
    let attachmentPresentation (ctx: ViewContext) (attachment: AttachmentView) : XmlNode =
        match attachment.Preview with
        | Video posterUrl -> videoPreview attachment posterUrl
        | Image thumbUrl -> lightboxedImagePreview attachment thumbUrl
        | File -> fileLink ctx attachment

    /// `message_presentation(message)`.
    let presentation (ctx: ViewContext) (message: MessageView) : XmlNode =
        match message.Content with
        | MessageContent.Attachment attachment -> attachmentPresentation ctx attachment
        | MessageContent.Sound sound -> soundPresentation sound
        | MessageContent.Text html -> Html.trusted html
        // `messages/_message` renders `messages/_unrenderable` instead.
        | MessageContent.Unrenderable -> Text.empty
