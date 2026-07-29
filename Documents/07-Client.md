# 7 — Client

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

## 7.1 Who the Client Is

**Mechelle Chapman** agreed to act as the external client tester for KSE: someone outside of the
development process who tests the finished build from a user's point of view rather than a developer's,
and whose feedback is recorded separately from the author's own notes elsewhere in this document set.

## 7.2 Why an External Client Tester

Every other piece of testing evidence in this document set — the "Known Issues" in `03-Implementation.md`
§3.2, the limitations recap in `06-Report.md` §6.5 — reflects the developer's own observations while
building and running the software. That's useful, but it's a poor substitute for watching someone hit the
login screen, the quality-tier picker, or the logout flow for the first time with no prior explanation of
how any of it works. Mechelle Chapman was brought in specifically to close that gap: to test the final
`run_me.exe` build cold, the way any real first-time user would.

## 7.3 Client Brief

Before testing, Mechelle was handed the finished `run_me.exe` build (no source access, no walkthrough) and
asked to:

1. Create an account, close the app, then reopen it and log back in with the same credentials.
2. Deliberately try an incorrect password once, to see what happens.
3. Play through the scene for a few minutes, including finding the indoor room (the console prints a hint:
   *"Fly to x=25 to find the indoor room"*).
4. Cycle through all four quality tiers (Low/Medium/High/Ray Tracing) with the Up/Down arrow keys and
   compare how the scene looks and feels at each.
5. Press **Esc** to log out mid-session and confirm the app returns to the login screen rather than closing.
6. Report anything that was confusing, broken, slow, or simply worth flagging, in their own words.

The technical results gathered during this session are recorded in [`08-Testing.md`](08-Testing.md); this
document only covers the client relationship and brief, not the test outcomes themselves.

## 7.4 Client Agreement

The following is the sign-off template used to record Mechelle's agreement to take part. **This is a
template for a physical/written signature — it is not filled in here, since only Mechelle can actually
agree to it:**

> I, Mechelle Chapman, agree to act as the independent client tester for the KingStudio Engine (KSE)
> project: to test the finished build described in `README.md` and give honest feedback on it, understanding
> that this is a personal, non-commercial coursework project rather than a commercial product.
>
> Signature: ________________________________  Date: ____________

## 7.5 Client Feedback

*To be completed once the testing session in `08-Testing.md` has taken place and Mechelle has reported
back.* Recording this separately from the developer's own test log (§7.2) is the point of having a client
in the first place — filling it in with invented feedback here would defeat that purpose, so it is left
blank pending the real session.

| Category | Mechelle's comment | Developer response |
|---|---|---|
| Login/sign-up | *pending* | |
| Controls/movement | *pending* | |
| Quality tiers | *pending* | |
| Logout (Esc) | *pending* | |
| Anything else | *pending* | |
