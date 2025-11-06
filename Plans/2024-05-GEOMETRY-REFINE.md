# Geometry Measurement Refactor Notes

- Launcher layout now delegates suggestion reservation and history clamping to `LayoutMeasurementUtility`.
- Next step: apply utility to standard terminal measurements (if any height heuristics remain) and audit animation entry points.
- Future idea: expose measurement snapshots for unit testing outside launcher.
