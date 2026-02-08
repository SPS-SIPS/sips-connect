# SIPS Monorepo Migration Notes

## Migration Overview
On February 8th, 2026, the SIPS project was consolidated from multiple independent repositories into this single monorepo. This migration was performed using the `git subtree` strategy to guarantee full history preservation and audit traceability.

## Repository Lineage
The following repositories were merged into this structure:

| Component | Prefix Path | Original Branch | Provenance Log |
| :--- | :--- | :--- | :--- |
| **SIPS.Connect** | `/SIPS.Connect` | `main` | Full history merged via subtree |
| **Packages** | `/Packages` | `improvements` | Full history merged via subtree |
| **Examples (Consumer)** | `/Examples/SIPS.Example.Consumer` | `main` | Full history merged via subtree |
| **Examples (Deployment)** | `/Examples/SIPS.Example.Deployement` | `main` | Full history merged via subtree |
| **Dashboard UI** | `/sps-dashboard-ui` | `feature` | Full history merged via subtree |

## Provenance Verification
To verify the merged history and lineage, auditors can run:
```bash
git log --graph --oneline --all
```
This command will display the convergence of individual project histories into the current unified main branch.

---
*Migration conducted by: Antigravity*
*Date: 2026-02-08*
