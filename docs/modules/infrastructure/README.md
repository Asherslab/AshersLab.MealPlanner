---
title: Infrastructure
kind: reference
status: current
module: infrastructure
verified: 2026-10-21
code:
  - AshersLab.MealPlanner.Grpc/Program.cs
  - Dockerfile
  - mealplanner
---

# Infrastructure

How the one process is put together, and how it is packaged and run.

- [How the app is hosted](hosting.md) — the projects, the request pipeline and its traps, the app shell, where
  the data lives.
- [How the app is deployed](deployment.md) — the image, the Home Assistant add-on and ingress, CI to GHCR,
  installing it, and the local ingress simulation.
