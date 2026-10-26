# Weightlifting warmup timer
# Contact dyaballikl@gmail.com for permissions
# Not to be copied or redistributed without permissions and attribution

---

## Comp OWL — Weightlifting Comp Warmup (Mobile & Tablet App)

**Comp OWL** (**Comp**etition **O**lympic **W**eight**L**ifting) is a cross-platform **.NET MAUI** mobile and tablet application targeting **Android (Phones & Tablets)** and **iOS (iPhones & iPads)**, ported from the original `.NET WinForms` [Weightlifting_Comp_Warmup](https://github.com/dyaballikl/Weightlifting_Comp_Warmup) desktop application to help warm up a lifter in Snatch and Clean & Jerk for Olympic weightlifting competition.

---

## Solution Structure

- `src/WeightliftingCompWarmup.Core/`
  - Pure C# domain library containing:
    - [`DataTypes.cs`](./src/WeightliftingCompWarmup.Core/Models/DataTypes.cs) — `Profile`, `WarmupStep`, `ExtraItem`, `WeightJumpRule`, `WarmupTimeRule`, and `LiftType`.
    - [`Defaults.cs`](./src/WeightliftingCompWarmup.Core/Services/Defaults.cs) — Baseline defaults and pre-configured `"Demo Profile"` matching the WinForms app.
    - [`WarmupCalculator.cs`](./src/WeightliftingCompWarmup.Core/Services/WarmupCalculator.cs) — Warmup weight progression generator (`CalculateWarmupWeights`), two-phase jump threshold lookup (`GetJumpForWeight`), final-jump midpoint smoothing (`SmoothLastJumps`), and reverse countdown calculation (`BuildSteps` / `BuildLiveSteps`).
    - [`BarbellLoadCalculator.cs`](./src/WeightliftingCompWarmup.Core/Services/BarbellLoadCalculator.cs) — IWF color-coded plate and 2.5kg competition collar layout engine (`Calculate`), including full-size plastic 5kg white and 2.5kg red technique plate rules below the green 10kg plate minimum and fractional change plates placed outside the collar.
    - [`LiveWarmupSession.cs`](./src/WeightliftingCompWarmup.Core/Services/LiveWarmupSession.cs) — Stateful 1-second tick engine for **Snatch LIVE** (competition start countdown, presentation & pre-snatch clock, and stage lift countdown) and **Clean & Jerk LIVE** (3-phase countdown: Snatch Attempts Remaining → Break After Snatch → C&J Stage Lifts).
    - [`ProfileStorageService.cs`](./src/WeightliftingCompWarmup.Core/Services/ProfileStorageService.cs) — Local JSON persistence for profiles (Load, Add, Duplicate, Rename, Delete, Save, and Clear All Settings).
- `src/WeightliftingCompWarmup.Maui/`
  - Cross-platform `.NET MAUI` mobile & tablet UI:
    - [`Views/MainPage.cs`](./src/WeightliftingCompWarmup.Maui/Views/MainPage.cs) — Adaptive responsive UI that dynamically switches between a **2-Column Split Dashboard** on Tablets/iPads (`>= 760dp`) and a **Segmented Compact Layout** on Phones (`< 760dp`).
    - [`Views/HelpAboutModalPage.cs`](./src/WeightliftingCompWarmup.Maui/Views/HelpAboutModalPage.cs) — Tabbed **How to Use (Help)** and **About Comp OWL** modal, shown automatically on first launch and accessible anytime via the `❓` button in the header bar.
    - [`Controls/BarbellPlateView.cs`](./src/WeightliftingCompWarmup.Maui/Controls/BarbellPlateView.cs) — Custom `GraphicsView` + `IDrawable` vector renderer supporting both Full Two-Sided Opener Barbells and Compact Single-Sleeve Lift Row graphics.
    - [`Controls/ScrubbableProgressBar.cs`](./src/WeightliftingCompWarmup.Maui/Controls/ScrubbableProgressBar.cs) — Touch-scrubbable progress bar allowing coaches/athletes to tap or drag directly on any stage or break progress bar to adjust elapsed time.
    - [`Services/DevicePlatformService.cs`](./src/WeightliftingCompWarmup.Maui/Services/DevicePlatformService.cs) — Screen wake-lock (`DeviceDisplay.Current.KeepScreenOn`) and 750Hz/600ms step transition audio beep + haptic alert.
    - [`ViewModels/MainViewModel.cs`](./src/WeightliftingCompWarmup.Maui/ViewModels/MainViewModel.cs) — Full MVVM bindings and commands for Setup and Live modes.
- `tests/WeightliftingCompWarmup.Tests/`
  - xUnit test suite verifying weight calculations, midpoint jump smoothing, IWF plate loading, Snatch & C&J live state machines, and profile CRUD persistence.

---

## Building & Running

### 1. Run Unit Tests
```powershell
dotnet test
```

### 2. Package Android APK (`package-apps.ps1`)
```powershell
powershell -ExecutionPolicy Bypass -File .\package-apps.ps1 -AndroidOnly
```

---

## License

Licensed under the **GNU General Public License v3.0** — see [LICENSE](./LICENSE) for details.
Contact `dyaballikl@gmail.com` for permissions.
