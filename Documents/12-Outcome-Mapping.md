# 12 — Outcome Mapping

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

Maps each HSC Software Engineering outcome to the specific document/section (or code) in this repository
that satisfies it — more specific than the generic deliverable-category mapping in the assessment brief,
so an assessor can go straight to the evidence.

| Outcome | Description | Deliverable(s) | Evidence in this repository |
|---|---|---|---|
| SE-12-01 | Justifies methods used to plan, develop and engineer software solutions | Report | `06-Report.md` §6.7 (methodology, design-tool, and technology justifications); `09-Project-Plan.md` §9.1 (WAgile justification) |
| SE-12-02 | Apply structural elements to develop programming code | Code | Five-project solution (`Engine.RHI`, `Engine.RHI.Vulkan`, `Engine.RHI.Direct3D12`, `Engine.Windowing`, `samples/Sandbox`), divided into interfaces/classes per `03-Implementation.md`; `11-Design.md` §11.1 |
| SE-12-03 | Analyses how current hardware, software and emerging technologies influence the development of software engineering solutions | Design, Report | `02-Research.md` (API/hardware/language research); `08-Testing.md` §8.2/§8.4 (hardware-driven performance analysis); `06-Report.md` §6.7 |
| SE-12-04 | Evaluates practices to safely and securely collect, use and store data | Design, Code, Report | `11-Design.md` §11.2–§11.3 (IPO chart + security design); `PasswordHasher.cs`/`AccountStore.cs`; `06-Report.md` §6.7 |
| SE-12-05 | Explains the social, ethical and legal implications of software engineering on the individual, society and the environment | Report | `06-Report.md` §6.7 (ethical considerations) |
| SE-12-06 | Justifies the selection and use of tools and resources to design, develop, manage and evaluate software | Report | `06-Report.md` §6.7 (design/dev/testing tool comparisons); `02-Research.md` §2.9 |
| SE-12-07 | Designs, develops and implements safe and secure programming solutions | Design, Code | `PasswordHasher.cs` (salted PBKDF2-HMAC-SHA256), `AccountStore.cs` (generic failure messages, input validation), `LoginScreen.cs` (masked input); `11-Design.md` §11.2–§11.3 |
| SE-12-08 | Tests and evaluates language structures to refine code | Tests | `tests/Engine.Tests/PasswordHasherTests.cs` (5 unit tests); `08-Testing.md` (functional/performance/UAT testing) |
| SE-12-09 | Applies methods to manage and document the development of a software project | Project plan, weekly updates | `09-Project-Plan.md` (Gantt chart, weekly % complete updates); `04-Development-Log.md`; `05-Timesheet.md` |

## Deliverable-to-Document Index

For the assessment brief's own deliverable categories, in submission order:

| Deliverable | Marks | Document(s) |
|---|---|---|
| Project identification | 0 | `07-Client.md` §7.1–§7.4 (client profile, brief, agreement/email template) |
| Project plan and weekly updates | 10 | `09-Project-Plan.md` |
| Requirements documentation | 5 | `10-Requirements.md` |
| Design documents | 10 | `11-Design.md` |
| Code | 45 | `src/`, `samples/Sandbox/`, `Engine.slnx` |
| Tests | 5 | `tests/Engine.Tests/`, `08-Testing.md` |
| Report | 25 | `06-Report.md` (§6.7 specifically addresses the Report deliverable's required points) |
