# Reading a feedback report

Use **More actions → Export feedback** in Windows or the browser image editor. Send the resulting report with the edition of the image or PDF that you reviewed. For a Word document or presentation, send the review PDF. A recipient can locate an error or requested change even after the note card has been collapsed.

The UTF-8 JSON object has `format: "inklane.feedback"` and `version: 1`. Check the SHA-256 of the supplied source bytes against `source.sha256` before interpreting any positions. The source record includes its filename and media type. A report for an Inklane notebook refers to a saved PDF revision.

| Field | Interpretation |
| --- | --- |
| `pages[].number` | One-based page number. An image has one page. |
| `width`, `height`, `unit` | Displayed page size. `image-pixel` uses the image after orientation. `dip-96` uses 96 units per inch on the displayed PDF page after crop and rotation. |
| `coordinateSystem` | `top-left`. X increases rightward and Y downward. Canvas zoom does not affect values. |
| `annotations[].id`, `type` | Object identity and type. A duplicated object has its own ID. |
| `note` | `x` and `y` position the marker. `width` and `height` size the card. `title` and `text` contain the explanation. |
| `text` | Placement, size, color, font size and editable text. |
| `ink` | Stroke width, color and ordered points with x, y and pressure. |
| `picture` | Placement, size and SHA-256 of the inserted PNG bytes. |

For example, to list each pinned explanation and its target with [jq](https://jqlang.org/):

```sh
jq '.pages[] | .number as $page | .annotations[] | select(.type == "note") | {page: $page, x, y, title, text}' drawing.png.feedback.json
```

The report includes all editable marks, including existing writing in a notebook. The original picture, PDF page text, embedded image bytes and directory paths are excluded. The recipient needs the identified source to see what the marks refer to. Keep the editable notebook or the original and its Inklane sidecar to resume editing.
