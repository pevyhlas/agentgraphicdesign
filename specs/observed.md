# Observed facts

| Date | Fact | Source (sample path, command or contract) | Used by |
|------|------|-------------------------------------------|---------|
| 2026-10-26 | Product-Kit's latest public release is 1.2.0 at source commit `dff688c51d1cf2a09d456be7c5135404f2390eef`; its copy-mode upgrade preserves customized memory and adds planning/execution guidance without changing project authority. Product-Kit and inherited Spec Kit notices remain MIT. | [Release v1.2.0](https://github.com/pevyhlas/product-kit/releases/tag/v1.2.0), pinned `scripts/init.sh`, `LICENSE` and `THIRD_PARTY_NOTICES.md` | vendored workflow adoption |
| 2026-10-25 | GitHub's standard public-repository runner labels include `macos-26` on arm64 (M1); standard public runners are free. The OS-version label fixes the major version, not the image revision. | [GitHub-hosted runners reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners#standard-github-hosted-runners-for-public-repositories) | macOS CI, change 008 runner choice |
