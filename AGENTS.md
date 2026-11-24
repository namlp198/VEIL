# AGENTS.md

## Role
You are an autonomous AI coding agent (Antigravity / Codex) working inside VEIL.

## Mandatory reading order
1. `README.md`
2. `docs/README_AI_WORKFLOW.md`
3. `docs/00_CONTEXT.md`
4. `tasks/active.md`
5. `handoff/latest.md`
6. `decisions/0000_INDEX.md`

## Rules
- Do not alter architecture without updating `decisions/`.
- Output intermediate documents to `outputs/` subdirectories.
- Always update `handoff/latest.md` before concluding.

## Git workflow (bắt buộc — chuẩn tại 06_Repositories/_standards/GIT-WORKFLOW.md)

- Nhánh: `main` (trunk ổn định), `develop`, `feature/*`, `fix/*`, `release/x.y`, `hotfix/x.y-*`.
- **Chỉ làm việc trên `develop`, `feature/*`, `fix/*`, `hotfix/*`.** Tuyệt đối không commit, push, merge cục bộ hay tag trên `main`/`release/*`. Nếu đang đứng ở nhánh đó, chuyển sang `develop` trước khi sửa file.
- Tag SemVer `vX.Y.Z` (firmware: `att-vX.Y.Z`, `sff-vX.Y.Z`, `mff-vX.Y.Z`), chỉ trên `release/*`.
- Nhánh AI tạo phải đặt tên `feature/*`, không dùng `claude/*`, `codex/*`.
- Đầu phiên: chạy `pwsh/powershell ..\_standards\scripts\Get-RepoSyncStatus.ps1 -Repo <tên repo>`; nếu `develop` có commit từ ngày trước chưa vào `main`, nhắc người dùng.
- Cuối phiên (hoặc khi task xong): nếu `develop` đi trước `main` và đủ điều kiện (tree sạch, đã push, CI xanh, không xung đột) thì **hỏi** người dùng có merge `develop → main` (qua PR) không. Không tự merge khi chưa được đồng ý trong phiên đó.
