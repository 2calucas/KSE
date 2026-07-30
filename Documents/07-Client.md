# 7 — Client

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

## 7.1 Who the Client Is

**Project identification:** the selected project is **KSE (KingStudio Engine)**, a Vulkan-based real-time
3D rendering engine with a secure local login/sign-up system gating access to its sample scene (see
`01-Statement-of-Intent.md`) — it falls under the assessment brief's "another option of your choice"
category, and satisfies that category's secure-storage requirement through salted, iterated password
hashing (`11-Design.md` §11.2–§11.3). **Michelle Chapman** agreed to act as the external client for the
project: someone outside of the development process, without a technical/software background, who tests
the finished build from a user's point of view rather than a developer's, and whose feedback is recorded
separately from the author's own notes elsewhere in this document set.

## 7.2 Why an External Client Tester

Every other piece of testing evidence in this document set — the "Known Issues" in `03-Implementation.md`
§3.2, the limitations recap in `06-Report.md` §6.5 — reflects the developer's own observations while
building and running the software. That's useful, but it's a poor substitute for watching someone hit the
login screen, the quality-tier picker, or the logout flow for the first time with no prior explanation of
how any of it works. Michelle Chapman was brought in specifically to close that gap: to test the final
`run_me.exe` build cold, the way any real first-time user would.

## 7.3 Client Brief

Before testing, Michelle was handed the finished `run_me.exe` build (no source access, no walkthrough) and
asked to:

1. Create an account, close the app, then reopen it and log back in with the same credentials.
2. Deliberately try an incorrect password once, to see what happens.
3. Play through the scene for a few minutes, including finding the indoor room (the console prints a hint:
   *"Fly to x=25 to find the indoor room"*).
4. Cycle through all four quality tiers (Low/Medium/High/Ray Tracing) with the Up/Down arrow keys and
   compare how the scene looks and feels at each.
5. Press **Esc** to log out mid-session and confirm the app returns to the login screen rather than closing.
6. Report anything that was confusing, broken, slow, or simply worth flagging, in her own words.

The technical results gathered during this session are recorded in [`08-Testing.md`](08-Testing.md); this
document only covers the client relationship and brief, not the test outcomes themselves.

## 7.4 Client Agreement

The following is the sign-off template used to record Michelle's agreement to take part. **This is a
template for a physical/written signature — it is not filled in here, since only Michelle can actually
agree to it:**

> I, Michelle Chapman, agree to act as the independent client tester for the KingStudio Engine (KSE)
> project: to test the finished build described in `README.md` and give honest feedback on it, understanding
> that this is a personal, non-commercial coursework project rather than a commercial product.
>
> Signature: ________________________________  Date: ____________

## 7.5 Client Feedback

Michelle completed part of the brief in §7.3 on 29/07/2026, under the developer's direct supervision. In
keeping with this document set's approach to recording things exactly as they happened rather than
smoothing them over (`01-Statement-of-Intent.md` §1.7), the session did not go entirely smoothly, and
that's recorded here rather than left out:

- **Context that matters to how the rest of this reads:** Michelle is 71, has not used a laptop in around
  5 years, and has not used a desktop keyboard-and-mouse setup in around 10 years. She came into this test
  with meaningfully less recent hands-on PC experience than the brief in §7.3 assumes, which shaped
  everything below.
- **Login/sign-up:** she created an account and logged back in with it successfully (§7.3, steps 1–2) —
  once the Tab-to-switch-field/type/Enter-to-submit flow was explained, this part did not present a
  lasting obstacle.
- **In-scene controls:** she had noticeable difficulty with the basic movement scheme — WASD, holding the
  right mouse button to look around, Space/Ctrl for vertical movement. This tracks with the experience gap
  above rather than pointing at anything specific to KSE's own control choices.
- **How she got through it:** she was able to navigate the scene, but only with active guidance —
  continuous verbal prompting from the developer, plus repeated reference to the control table in
  `README.md` → *How to use it* → *2. Move around the scene* — rather than working the controls out
  unaided from the on-screen input overlay alone.

| Category | Michelle's comment / observed behaviour | Developer response |
|---|---|---|
| Login/sign-up | Created an account and logged back in successfully once the Tab/type/Enter flow was explained. | No changes needed here — matches TC01/TC05 in `08-Testing.md` §8.3. |
| Controls/movement | Struggled with WASD + mouse-look initially; attributed to 5+ years without a laptop and 10+ years without a keyboard-and-mouse PC. Needed continuous guidance and repeated reference to the README control table to make progress. | The in-scene `InputOverlay` currently only shows a *live readout* of keys currently held (for debugging), not a static list of available controls for a new player — worth revisiting if a genuinely first-time, unassisted user is a real target audience. |
| Quality tiers | Not specifically exercised in this account of the session. | — |
| Logout (Esc) | Not specifically exercised in this account of the session. | — |
| Anything else | Overall: usable with sustained, hands-on guidance; not yet approachable for an unassisted, infrequent PC user. | This is a genuinely useful finding distinct from the functional pass/fail results in `08-Testing.md` §8.3 — those confirm the features work; this confirms they aren't yet easy for every kind of user to discover unaided. |

## 7.6 Client Willingness Email (Template)

The assessment brief asks for "an email from the client indicating their willingness to support you in the
project," submitted alongside the project identification paragraph in §7.1. The template below is what
that email should say once actually sent by Michelle from her own address — **it is not a real, sent email,
and is not filled in as one**, for the same reason the agreement in §7.4 is left as a template: only
Michelle can actually send it. Forward or re-key this into an email from her and attach that email (not
this file) as the submitted evidence.

> **Subject:** Happy to help test your engine project
>
> Hi Cal,
>
> Yes, I'm happy to be the client tester for your KSE project for your Software Engineering assessment.
> I understand this is a coursework project rather than a commercial product, and I'm willing to try out
> the finished build and give you honest feedback on it.
>
> Michelle
