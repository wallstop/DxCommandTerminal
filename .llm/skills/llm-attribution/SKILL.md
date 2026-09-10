---
name: llm-attribution
description: Disclose LLM-generated GitHub content posted through wallstop's account and never auto-respond to outside contributors. Use when posting or drafting any GitHub comment, issue, PR description, or review, and when any workflow could react to an external contributor's issue or PR.
metadata:
  category: Core
---

# LLM Attribution

All GitHub content posts through wallstop's account. Machine text must never read as
the human.

## Disclosure

LLM-generated comments, issues, PR descriptions, and reviews start with this exact
first line, before all other content:

```
DISCLOSURE: LLM-GENERATED TEXT
```

One line per artifact. Keep it when editing LLM-generated content; remove it only
after wallstop rewrites the text by hand.

## Outside contributors

1. Never reply to, close, label, approve, or merge an outside contributor's issue or
   PR without wallstop's instruction. Summarize the request and wait.
2. Drafted replies are allowed only when marked LLM-generated and gated on wallstop's
   approval.
3. wallstop's own issues and PRs may get routine agent updates, still disclosed.
