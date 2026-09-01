# Computer Use handoff schema

Use this reference only for a requested Computer Use handoff from a recorder session. The package is a reviewed procedure with evidence, not an executable macro.

Create `computer-use-handoff/workflow.md` and `computer-use-handoff/steps.json` beside the session unless the user specifies another destination. Do not copy sensitive visible text, passwords, tokens, or deliberately unrecorded typed text.

`steps.json` must include workflow title, purpose, target application, declared input variables, success criteria, and a step list. Each step needs an id, title, goal, preconditions, semantic action, strongest available locator, expected result, visible verification, fallback, safety (`read_only`, `edit`, `requires_confirmation`, or `unknown`), status (`ready` or `needs_review`), and evidence.

Use semantic actions such as `invoke`, `select`, `command_key`, `window_switch`, `type_variable`, `scroll`, `drag`, or `observe`. Prefer automation ID, control name, and control type over coordinates. Require named variables for replay-time text and state a visible verification condition. Set `needs_review` when evidence is ambiguous or incomplete.

Write `workflow.md` as a concise task procedure with its inputs, success criteria, numbered steps, fallbacks, and safety boundaries. End it by requiring the executing agent to inspect the live UI before every action and stop if the expected control, state, or safety boundary differs.
