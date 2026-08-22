# JSON Schema

BlazeForms publishes a [JSON Schema](https://json-schema.org/) (draft 2020-12) for the form
definition document — the same shape `BlazeForms.Serialization.FormJson` reads and writes. It is
generated, not hand-authored: `FormJsonSchema.CreateDefinitionSchema()` runs
[`JsonSchemaExporter`](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/schema-export)
over the library's own serializer options, so the schema can never describe a shape the
serializer doesn't actually produce.

## Using the schema

**In an editor (VS Code).** Map the schema to your definition files in `settings.json`:

```json
{
  "json.schemas": [
    {
      "fileMatch": ["*.form.json"],
      "url": "https://dannydel.github.io/blaze-forms/schemas/form-definition-v3.schema.json"
    }
  ]
}
```

**In a definition file.** Add a `$schema` property so any conforming tool picks it up without
extra configuration:

```json
{
  "$schema": "https://dannydel.github.io/blaze-forms/schemas/form-definition-v3.schema.json",
  "schemaVersion": 3,
  "id": "...",
  "name": "..."
}
```

`$schema` is not itself a property of `FormDefinition` — `FormJson.DeserializeDefinition` ignores
unrecognized top-level properties, so adding it does not break round-tripping. It also does not
survive a round-trip: `FormJson.SerializeDefinition` never writes it back out, since it isn't a
member of `FormDefinition`. Treat it as authoring-time metadata for your editor, not part of the
document's persisted content.

The schema is also **stricter than the reader** in one respect: `TreatNullObliviousAsNonNullable`
makes the exporter mark most reference-typed properties non-nullable (matching the C# nullable
annotations `FormDefinition` declares), but `FormJson.DeserializeDefinition` is more forgiving at
runtime than that — a schema-version-1 document, for instance, predates several later properties
and reads fine despite omitting them. A document that fails schema validation may still be one
this build accepts; the schema is an authoring aid and a shape contract, not the full definition
of "readable."

## `$id` and versioning: add-only once published, mutable until then

Each schema file's `$id` embeds the `schemaVersion` it describes
(`form-definition-v3.schema.json` for version 3). The publish policy has two states:

- **Unpublished** — a schema version with no tagged release and not yet live at its `$id` URL.
  Its file may be freely regenerated in place as `FormDefinition` (or a type it references)
  changes during development; there is no external consumer yet to break.
- **Published** — once a version has shipped in a tagged release or gone live under its `$id`
  URL, its file is frozen, the same rule as the wire format itself (AGENTS.md invariant #3). It
  is never edited in place again — not even to fix an exporter-output quirk. A consumer that
  pinned a published `$id` must keep seeing exactly what it saw on day one. Two cases follow from
  this:
  - An **exporter-output-only change** (e.g. an SDK bump alters `JsonSchemaExporter`'s emitted
    shape with no change to `FormDefinition` itself) to an already-published version must **not**
    be republished. Pin the SDK version that produced the published file, or otherwise work around
    the drift in code — never push a corrected file out under the same `$id`.
  - A **semantic change** (a real change to what a definition of that version may contain) always
    requires a new `schemaVersion` and a new file (`form-definition-v4.schema.json`, and so on) —
    never an edit to the published one.

The file lives at `docs/schemas/form-definition-v3.schema.json` in this repo and is published
to the same path under `https://dannydel.github.io/blaze-forms/schemas/` by `pages.yml`, and
attached to every GitHub release by `release.yml`.

## What the schema deliberately cannot express

JSON Schema validates shape, not authoring rules. Several things a definition must satisfy are
intentionally left out, and remain `FormLinter`'s job instead:

- **Per-`NodeType` conditional requirements** — e.g. a `select` node needing a non-empty
  `options` array, or a `number` node's `min` being less than its `max`. Expressing this
  generically needs `allOf`/`if`-`then` schema composition keyed on the `type` discriminator; the
  linter's targeted rules are easier to read, test, and extend than the schema composition would
  be.
- **Envelope `values`.** A submission envelope's `values` map is exported as an unconstrained
  `{"type": "object"}` — the value shape varies per `NodeType` and isn't part of the definition
  schema at all.
- **Cross-field consistency** — e.g. a `visibleWhen` condition referencing a node ID that exists
  elsewhere in the document. The schema has no way to see across the tree for this; it's a lint,
  not a shape constraint.

## Reading the shape: pointer `$ref`s, not `$defs`

`FormNode.Children` is recursive, so the exported schema contains JSON-pointer `$ref`s back into
the document (e.g. `"$ref": "#/properties/pages/items/properties/sections/items/properties/nodes/items"`)
rather than named `$defs`. This is an artifact of how `JsonSchemaExporter` represents recursive
CLR types, not a hand-authoring choice — don't refactor it to `$defs` without checking whether a
newer SDK's exporter changed its own convention first.

## Regenerating the schema

The schema is pinned by a golden file, exactly like the wire format
(`tests/BlazeForms.Core.Tests/GoldenFileTests.cs`):

```bash
BLAZEFORMS_UPDATE_GOLDEN=1 dotnet test tests/BlazeForms.Core.Tests
```

This rewrites **both** `tests/BlazeForms.Core.Tests/Golden/form-definition-v3.schema.json` and
its `docs/schemas/form-definition-v3.schema.json` copy together, so the two can never desync (a
test asserts they are byte-identical). **Read the diff before committing it — and check the
publish policy above first**: only regenerate the file for the version currently unpublished. If
`form-definition-v3` has already shipped in a release or gone live at its `$id` URL, do not run
this against it; cut `form-definition-v4` instead.

- **Changed `FormDefinition` (or a type it references), version still unpublished.** The diff
  should be exactly the schema change you intended. If the change is semantic, it also needs a
  `schemaVersion` bump and a new golden wire-format file — the expected, healthy case.
- **Changed `FormDefinition`, version already published.** Do not regenerate the published file.
  Bump `schemaVersion`, and the regeneration above will target the new, still-unpublished file
  instead.
- **Non-empty diff with no code change** (e.g. a .NET SDK bump). `JsonSchemaExporter`'s output
  shape changed underneath you. Read the diff line by line — a changed schema without a
  `schemaVersion` bump is a finding to investigate, not a formality to wave through. If the
  version is unpublished, the same regeneration commits the accepted diff; if it's published, see
  the exporter-output-only case above instead — never edit the published file.

## External validation

Because the exporter could in principle emit output that is valid JSON but not a *valid schema*,
this is checked with an external validator rather than trusted on the exporter's say-so:

```bash
npx --yes ajv-cli validate \
  --spec=draft2020 \
  -s docs/schemas/form-definition-v3.schema.json \
  -d tests/BlazeForms.Core.Tests/Golden/form-definition-v3.json
```

This validates the golden representative definition (schema version 3) against the schema that
claims to describe it.
