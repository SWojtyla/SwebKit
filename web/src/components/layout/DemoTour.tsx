import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { DEMO_TOUR_STEPS, useDemoTourStore } from "@/lib/stores/demo-tour";
import { useDemoMode, useToggleDemoMode } from "@/lib/hooks";
import { Play, ChevronLeft, ChevronRight, X, MapPin } from "lucide-react";

function TourSpotlight({ target }: { target?: string }) {
  const [rect, setRect] = useState<DOMRect | null>(null);

  useEffect(() => {
    if (!target) return;
    const update = () => {
      const el = document.querySelector(target);
      setRect(el?.getBoundingClientRect() ?? null);
    };
    update();
    window.addEventListener("resize", update);
    window.addEventListener("scroll", update, true);
    const id = setInterval(update, 500);
    return () => {
      window.removeEventListener("resize", update);
      window.removeEventListener("scroll", update, true);
      clearInterval(id);
    };
  }, [target]);

  if (!rect) return null;

  return (
    <>
      <div
        className="pointer-events-none absolute rounded-lg ring-2 ring-primary ring-offset-2 ring-offset-background"
        style={{
          left: rect.left - 8,
          top: rect.top - 8,
          width: rect.width + 16,
          height: rect.height + 16,
        }}
      />
      <svg className="pointer-events-none absolute inset-0 h-full w-full">
        <defs>
          <mask id="demo-tour-mask">
            <rect x="0" y="0" width="100%" height="100%" fill="white" />
            <rect x={rect.left - 8} y={rect.top - 8} width={rect.width + 16} height={rect.height + 16} rx="8" fill="black" />
          </mask>
        </defs>
        <rect x="0" y="0" width="100%" height="100%" fill="rgba(0,0,0,0.25)" mask="url(#demo-tour-mask)" />
      </svg>
    </>
  );
}

export function DemoTour() {
  const navigate = useNavigate();
  const { isRunning, stepIndex, next, previous, stop } = useDemoTourStore();
  const { data: demoMode } = useDemoMode();
  const toggleDemo = useToggleDemoMode();
  const enabledRef = useRef(false);

  const step = DEMO_TOUR_STEPS[stepIndex];

  useEffect(() => {
    if (!isRunning) {
      enabledRef.current = false;
      return;
    }
    if (demoMode && !demoMode.isDemoMode && !enabledRef.current && !toggleDemo.isPending) {
      enabledRef.current = true;
      toggleDemo.mutate(true);
    }
  }, [isRunning, demoMode, toggleDemo]);

  useEffect(() => {
    if (isRunning && step) {
      navigate(step.route);
    }
  }, [isRunning, stepIndex, navigate, step]);

  if (!isRunning || !step) return null;

  const progress = Math.round(((stepIndex + 1) / DEMO_TOUR_STEPS.length) * 100);

  return (
    <div className="pointer-events-none fixed inset-0 z-[100]" data-testid="demo-tour-overlay">
      <TourSpotlight target={step.target} />
      <div className="pointer-events-auto fixed bottom-6 left-1/2 w-[min(28rem,calc(100%-2rem))] -translate-x-1/2 rounded-xl border bg-card p-4 shadow-2xl" data-testid="demo-tour-card">
        <div className="mb-2 flex items-center justify-between">
          <div className="flex items-center gap-2 text-sm font-semibold text-primary">
            <MapPin className="h-4 w-4" />
            <span data-testid="demo-tour-step-title">{step.title}</span>
          </div>
          <button
            onClick={stop}
            className="rounded-md p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
            data-testid="demo-tour-stop"
            aria-label="Stop tour"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="mb-4 text-sm text-muted-foreground" data-testid="demo-tour-step-description">{step.description}</p>
        <div className="mb-3 h-1.5 w-full overflow-hidden rounded-full bg-muted">
          <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
        </div>
        <div className="flex items-center justify-between">
          <span className="text-xs text-muted-foreground" data-testid="demo-tour-progress">
            {stepIndex + 1} / {DEMO_TOUR_STEPS.length}
          </span>
          <div className="flex items-center gap-2">
            <button
              onClick={previous}
              disabled={stepIndex === 0}
              className="flex items-center gap-1 rounded-md border px-2.5 py-1.5 text-sm hover:bg-accent disabled:opacity-50"
              data-testid="demo-tour-previous"
            >
              <ChevronLeft className="h-3.5 w-3.5" /> Back
            </button>
            <button
              onClick={next}
              className="flex items-center gap-1 rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90"
              data-testid="demo-tour-next"
            >
              {stepIndex === DEMO_TOUR_STEPS.length - 1 ? "Finish" : "Next"} <ChevronRight className="h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export function StartDemoTourButton({ className }: { className?: string }) {
  const start = useDemoTourStore((s) => s.start);
  return (
    <button
      onClick={start}
      className={`inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 ${className ?? ""}`}
      data-testid="demo-tour-start"
    >
      <Play className="h-3.5 w-3.5" /> Start demo tour
    </button>
  );
}
