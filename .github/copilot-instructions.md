# Copilot Instructions

## Project Guidelines
- User expects actual code-level adoption of Microsoft.Extensions.AI APIs, not only package-level changes or planning artifacts.

## Model Usage Guidelines
- Use the deepseek-v4-pro reasoning model via the raw SSE path; do not use IChatClient + UseFunctionInvocation.
- Only utilize IChatClient for non-pro models like deepseek-v4-flash.