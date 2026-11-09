# Inklane

- The Windows 11 desktop app and the shared web workspace use the same annotation concepts. Platform hosts live in `platforms/`.
- Write repository documentation and development communication in English.
- Follow the device language, with English fallback and German support.
- Protect user documents and existing work.
- Never claim unverified builds, tests, or functionality.
- Keep temporary agent work in the git-ignored `.scratch/` directory; it is not authoritative project documentation.
- Prefer maintainable, minimal solutions over unnecessary complexity.
- Use meaningful Conventional Commits.
- The repository and GitHub Pages site are public. Keep user documents and local QA data out of commits.
- Keep `main` as the integrated development line. Use short-lived `feature/`, `fix/`, or `refactor/` branches. Keep a release branch only while it has a separate maintenance or development purpose. Use annotated tags for shipped versions.
- Integrate through pull requests after the applicable platform and website checks pass. Squash cohesive changes with incidental commits, rebase or fast-forward a curated linear series, and use a merge commit when branch structure carries useful history. Remove a merged branch once it has no remaining work.
- Use the repository owner's associated GitHub noreply address for work on behalf of that account. Add co-author trailers only for distinct, genuine contributors. Keep released tags on the commits used to build the published binaries.
- After a verified Windows change lands on `main`, run `./scripts/install-verified.ps1` from the integrated checkout on the Windows review machine. Use the per-user installer to update the registered application and verify the actual Start shortcut. Close the installed app normally before upgrading so pending work can be saved. Run destructive installer lifecycle checks in CI or a clean Windows profile.
- A local development upgrade uses an integrated build. Publishing installers, creating releases, and pushing release tags require separate approval. Keep review data, planning and recovery backups out of public refs and issues.
