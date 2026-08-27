# Computer Use handoff schema

Use this reference only for a requested Computer Use handoff from a recorder session. The package is a reviewed procedure with evidence, not an executable macro.

## Package files

Create these files in `computer-use-handoff/` beside the session unless the user specifies another location:

- `workflow.md` — compact, reader-facing procedure and input list.
- `steps.json` — structured action, verification, safety, and evidence data.

Do not copy sensitive visible text, passwords, tokens, or unrecorded typed text into either file.

## `steps.json` shape

```json
{
  "schema_version": 1,
  "workflow": {
    "title": "Short task name",
    "purpose": "What this accomplishes",
    "target_application": "App name or executable",
    "variables": [
      {
        "name": "customer_name",
        "description": "Customer to find",
        "required": true,
        "sensitive": false
      }
    ],
    "success_criteria": ["Visible final state that proves completion"]
  },
  "steps": [
    {
      "id": "step-01",
      "title": "Open customer search",
      "goal": "Reach the customer search field.",
      "preconditions": ["The application is open", "The sidebar is visible"],
      "action": {
        "kind": "invoke",
        "locator": {
          "application": "example-app",
          "window_title_hint": "Customers",
          "control_name": "Search customers",
          "control_type": "edit",
          "automation_id": "CustomerSearch",
          "framework": "WPF",
          "coordinate_hint": null
        },
        "input_variable": null
      },
      "expected_result": "The customer search field has focus.",
      "verification": {
        "kind": "visible_control",
        "description": "A search field named Search customers is visible and focused."
      },
      "fallback": "If the sidebar is collapsed, expand it and locate Customers again.",
      "safety": "read_only",
      "status": "ready",
      "evidence": {
        "event_step": 4,
        "screenshot": "../screenshots/step-0004-after.png",
        "screenshot_source": "window-handle"
      }
    }
  ]
}
```

## Field rules

- `action.kind`: use a semantic term such as `invoke`, `select`, `command_key`, `window_switch`, `type_variable`, `scroll`, `drag`, or `observe`. A compact pointer path may be included as supporting evidence, but do not use it as a replay coordinate. Do not use `click` when the intended interaction is known.
- `locator`: include only values supported by the event's UI Automation control, window metadata, or screenshot. Prefer `automation_id`, control name, and type over `coordinate_hint`.
- `input_variable`: name a declared workflow variable whenever the step needs text or a file chosen at replay time. The recorder intentionally excludes ordinary typed text.
- `verification`: state a current, visible condition—not merely that the input was sent.
- `safety`: one of `read_only`, `edit`, `requires_confirmation`, or `unknown`.
- `status`: use `ready` only when the locator and expected result have adequate evidence. Use `needs_review` for missing UI Automation data, ambiguous screenshots, unclear conditions, or app states that can vary.
- `evidence`: must point to the originating event step and an existing session-relative screenshot when available. Leave `screenshot` null and explain the limitation in `workflow.md` when no reliable image exists.

## `workflow.md` requirements

Write for the task that will run the procedure:

1. State purpose, target application, required variables, and success criteria.
2. List numbered, semantic steps with their precondition and expected result.
3. Include fallbacks and the step safety level where relevant.
4. End with a reminder: inspect the live UI before each action; do not act if the control, expected state, or safety boundary differs.

Avoid describing the recording, screenshot capture, event stream, or UI Automation implementation in the reader-facing procedure. Keep evidence identifiers in `steps.json`.
