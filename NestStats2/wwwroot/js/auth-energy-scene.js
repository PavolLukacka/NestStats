(() => {
    const visuals = Array.from(document.querySelectorAll(".pv-visual"));
    if (visuals.length === 0) {
        return;
    }

    const canAnimate = typeof Element !== "undefined" && !!Element.prototype.getAnimations;
    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const lerp = (from, to, t) => from + (to - from) * clamp(t, 0, 1);
    const smoothstep = (edge0, edge1, value) => {
        const t = clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    };

    function kwAtProgress(p) {
        if (p < 0.04) return { value: lerp(1.0, 1.8, p / 0.04), mode: "solar" };
        if (p < 0.12) return { value: lerp(1.8, 5.0, (p - 0.04) / 0.08), mode: "solar" };
        if (p < 0.24) return { value: lerp(5.0, 8.8, (p - 0.12) / 0.12), mode: "solar" };
        if (p < 0.32) return { value: lerp(8.8, 10.0, (p - 0.24) / 0.08), mode: "solar" };
        if (p < 0.38) return { value: 10.0, mode: "solar" };
        if (p < 0.45) return { value: lerp(10.0, 4.4, (p - 0.38) / 0.07), mode: "solar" };
        if (p < 0.50) return { value: lerp(4.4, 1.0, (p - 0.45) / 0.05), mode: "solar" };
        if (p < 0.56) return { value: lerp(1.0, 2.8, (p - 0.50) / 0.06), mode: "battery" };
        if (p < 0.78) return { value: lerp(2.8, 2.0, (p - 0.56) / 0.22), mode: "battery" };
        if (p < 0.90) return { value: lerp(2.0, 1.2, (p - 0.78) / 0.12), mode: "battery" };
        if (p < 0.96) return { value: lerp(1.2, 1.0, (p - 0.90) / 0.06), mode: "battery" };
        return { value: lerp(1.0, 1.8, (p - 0.96) / 0.04), mode: "solar" };
    }

    function initPvCycleBar(visual) {
        if (visual.dataset.pvTimelineReady === "true") {
            return;
        }

        const bar = visual.querySelector(".js-pv-cycle-bar");
        const track = visual.querySelector(".js-pv-cycle-track");
        const fill = visual.querySelector(".js-pv-cycle-fill");
        const head = visual.querySelector(".js-pv-cycle-head");
        if (!bar || !track || !fill || !head) {
            return;
        }

        visual.dataset.pvTimelineReady = "true";

        const cycleStr = getComputedStyle(visual).getPropertyValue("--pv-cycle").trim();
        const cycleS = parseFloat(cycleStr) || 34;
        const cycleMsTotal = cycleS * 1000;
        const kwEl = visual.querySelector(".pvs-inverter__kw");
        const modeEl = visual.querySelector(".pvs-inverter__mode");

        let currentT = 0;
        let lastTs = null;
        let isScrubbing = false;
        let cycleAnims = null;

        head.style.animation = "none";

        function getCycleAnims() {
            if (cycleAnims) {
                return cycleAnims;
            }

            if (!canAnimate) {
                cycleAnims = [];
                return cycleAnims;
            }

            const selectors = [
                ".pvs-sky", ".pvs-sun", ".pvs-sun__rays",
                ".pvs-cloud", ".pvs-ground",
                ".pvs-moon", ".pvs-stars",
                ".pvs-house__img", ".pvs-house__shine", ".pvs-house__glow",
                ".pvs-battery__fill", ".pvs-battery__bolt", ".pvs-battery__label",
                ".pvs-inverter__led",
                ".pvs-flow--solar", ".pvs-flow--charge", ".pvs-flow--night",
                ".pvs-timeline__bg"
            ];

            cycleAnims = [];
            selectors.forEach((selector) => {
                visual.querySelectorAll(selector).forEach((element) => {
                    element.getAnimations().forEach((animation) => {
                        try {
                            const timing = animation.effect?.getTiming?.();
                            const duration = Number(timing?.duration ?? 0);
                            if (Number.isFinite(duration) && Math.abs(duration - cycleMsTotal) < 800) {
                                cycleAnims.push(animation);
                            }
                        } catch {
                            // Ignore browser timing edge cases.
                        }
                    });
                });
            });

            return cycleAnims;
        }

        function syncStart() {
            if (!canAnimate) {
                return;
            }

            const sky = visual.querySelector(".pvs-sky");
            if (!sky) {
                return;
            }

            for (const animation of sky.getAnimations()) {
                try {
                    const timing = animation.effect?.getTiming?.();
                    const duration = Number(timing?.duration ?? 0);
                    const animationTime = Number(animation.currentTime ?? 0);
                    if (Number.isFinite(duration) && Math.abs(duration - cycleMsTotal) < 800 && animationTime >= 0) {
                        currentT = (animationTime / 1000) % cycleS;
                        break;
                    }
                } catch {
                    // Keep the zero start if the animation cannot be read.
                }
            }
        }

        function updateKw(p) {
            const reading = kwAtProgress(p);
            if (kwEl) {
                kwEl.textContent = `${reading.value.toFixed(1)} kW`;
                kwEl.style.color = reading.mode === "battery" ? "#a855f7" : "#10b981";
            }

            if (modeEl) {
                modeEl.textContent = reading.mode === "battery" ? "STORAGE" : "SOLAR";
                modeEl.style.color = reading.mode === "battery"
                    ? "rgba(168,85,247,.75)"
                    : "rgba(16,185,129,.75)";
            }
        }

        function updateSceneVars(p) {
            const sunset = smoothstep(0.34, 0.45, p) * (1 - smoothstep(0.48, 0.58, p));
            const night = smoothstep(0.45, 0.56, p) * (1 - smoothstep(0.86, 0.96, p));
            const dawn = p > 0.86
                ? smoothstep(0.86, 0.98, p)
                : (p < 0.12 ? 1 - smoothstep(0.02, 0.12, p) : 0);
            const warm = Math.max(sunset, dawn * 0.72);

            visual.style.setProperty("--pvs-sunset-alpha", warm.toFixed(3));
            visual.style.setProperty("--pvs-night-alpha", night.toFixed(3));
            visual.style.setProperty("--pvs-cycle-pct", `${(p * 100).toFixed(2)}%`);
        }

        function updateBar() {
            const p = currentT / cycleS;
            const pct = (p * 100).toFixed(2);

            fill.style.setProperty("--f", `${(100 - p * 100).toFixed(2)}%`);
            head.style.left = `${pct}%`;
            bar.setAttribute("aria-valuenow", String(Math.round(p * 100)));
            bar.setAttribute("aria-valuetext", `${Math.round(p * 100)} percent`);
            updateSceneVars(p);
            updateKw(p);
        }

        function seekAllTo(tSeconds) {
            const tMs = tSeconds * 1000;
            getCycleAnims().forEach((animation) => {
                try {
                    animation.currentTime = tMs;
                } catch {
                    // Some browser-created animations cannot be seeked.
                }
            });
        }

        function scrubTo(clientX) {
            const rect = track.getBoundingClientRect();
            const p = clamp((clientX - rect.left) / rect.width, 0, 1);
            currentT = p * cycleS;
            seekAllTo(currentT);
            updateBar();
        }

        function startScrub(clientX) {
            isScrubbing = true;
            bar.classList.add("is-scrubbing");
            getCycleAnims().forEach((animation) => {
                try {
                    animation.pause();
                } catch {
                    // Ignore animations that cannot be paused.
                }
            });
            scrubTo(clientX);
        }

        function endScrub() {
            if (!isScrubbing) {
                return;
            }

            isScrubbing = false;
            bar.classList.remove("is-scrubbing");
            getCycleAnims().forEach((animation) => {
                try {
                    animation.currentTime = currentT * 1000;
                    animation.play();
                } catch {
                    // Ignore animations that cannot be resumed.
                }
            });
        }

        bar.addEventListener("pointerdown", (event) => {
            event.preventDefault();
            bar.setPointerCapture?.(event.pointerId);
            startScrub(event.clientX);
        });

        bar.addEventListener("pointermove", (event) => {
            if (isScrubbing) {
                scrubTo(event.clientX);
            }
        });

        bar.addEventListener("pointerup", (event) => {
            bar.releasePointerCapture?.(event.pointerId);
            endScrub();
        });

        bar.addEventListener("pointercancel", endScrub);
        bar.addEventListener("lostpointercapture", endScrub);

        bar.addEventListener("wheel", (event) => {
            event.preventDefault();
            const rawDelta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
            const direction = rawDelta >= 0 ? 1 : -1;
            currentT = (currentT + direction * (cycleS / 70) + cycleS) % cycleS;
            seekAllTo(currentT);
            updateBar();
        }, { passive: false });

        bar.addEventListener("keydown", (event) => {
            const step = cycleS / 50;
            if (event.key === "ArrowRight" || event.key === "ArrowUp") {
                currentT = (currentT + step) % cycleS;
            } else if (event.key === "ArrowLeft" || event.key === "ArrowDown") {
                currentT = (currentT - step + cycleS) % cycleS;
            } else if (event.key === "Home") {
                currentT = 0;
            } else if (event.key === "End") {
                currentT = cycleS * 0.99;
            } else {
                return;
            }

            seekAllTo(currentT);
            updateBar();
            event.preventDefault();
        });

        function frame(ts) {
            if (!isScrubbing) {
                if (lastTs !== null) {
                    currentT = (currentT + Math.min((ts - lastTs) / 1000, 0.1)) % cycleS;
                }
                updateBar();
            }

            lastTs = ts;
            requestAnimationFrame(frame);
        }

        syncStart();
        updateBar();
        requestAnimationFrame(frame);
    }

    visuals.forEach(initPvCycleBar);
})();
