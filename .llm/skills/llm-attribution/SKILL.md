---
name: llm-attribution
description: Disclose LLM-generated content posted through wallstop's GitHub identity and never auto-respond to outside contributors. Use when posting or drafting any GitHub comment, issue, PR description, or review, and when any workflow could react to an external contributor's issue or PR.
metadata:
  category: Core
---

# LLM Attribution

All GitHub content posts through wallstop's credentials. Readers must never think they
are talking to the human when the text is machine-generated.

## Disclosure

1. Any LLM-generated comment, issue, PR description, or review starts with a
   disclosure line at the very top, before all other content:

   > This content is LLM-generated and posted through wallstop's account.

2. One disclosure covers only the text under it. Disclose every artifact separately.
3. Keep the disclosure when editing or refreshing LLM-generated content. Remove it
   only after wallstop rewrites the text by hand.

## No auto-responses

1. Never reply to, close, label, approve, or merge issues or PRs from outside
   contributors without wallstop's explicit instruction. Collect the request,
   summarize it for wallstop, and wait.
2. Do not answer other users' questions as wallstop. Drafting a reply is allowed
   only when the draft is marked LLM-generated and gated on wallstop's approval.
3. wallstop's own self-filed issues and PRs may receive routine status updates from
   an agent session, but those updates carry the same disclosure.
4. If an external contribution needs substantive review, prepare findings as a
   summary for wallstop. wallstop decides what is posted.
