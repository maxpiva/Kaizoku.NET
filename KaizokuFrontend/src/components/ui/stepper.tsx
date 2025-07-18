/* eslint-disable @typescript-eslint/no-unsafe-assignment */
/* eslint-disable @typescript-eslint/no-unsafe-member-access */
/* eslint-disable @typescript-eslint/no-unsafe-return */
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-empty-function */
"use client";

import { cva } from "class-variance-authority";
import { CheckIcon, Loader2, X, type LucideIcon } from "lucide-react";
import * as React from "react";

import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent } from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

// <---------- CONTEXT ---------->

interface StepperContextValue extends Omit<StepperProps, 'activeStep'> {
  clickable?: boolean;
  isError?: boolean;
  isLoading?: boolean;
  isVertical?: boolean;
  stepCount?: number;
  expandVerticalSteps?: boolean;
  activeStep: number;
  initialStep: number;
}

const StepperContext = React.createContext<
  StepperContextValue & {
    nextStep: () => void;
    prevStep: () => void;
    resetSteps: () => void;
    setStep: (step: number) => void;
  }
>({
  steps: [],
  activeStep: 0,
  initialStep: 0,
  nextStep: () => { },
  prevStep: () => { },
  resetSteps: () => { },
  setStep: () => { },
});

type StepperContextProviderProps = {
  value: Omit<StepperContextValue, "activeStep"> & { activeStep?: number };
  children: React.ReactNode;
};

const StepperProvider = ({ value, children }: StepperContextProviderProps) => {
  const isError = value.state === "error";
  const isLoading = value.state === "loading";
  // Use controlled or uncontrolled mode
  const [internalActiveStep, setInternalActiveStep] = React.useState(value.initialStep);
  const isControlled = value.activeStep !== undefined;
  const activeStep = isControlled ? (value.activeStep ?? 0) : internalActiveStep;

  const nextStep = () => {
    if (!isControlled) {
      setInternalActiveStep((prev) => prev + 1);
    }
  };

  const prevStep = () => {
    if (!isControlled) {
      setInternalActiveStep((prev) => prev - 1);
    }
  };

  const resetSteps = () => {
    if (!isControlled) {
      setInternalActiveStep(value.initialStep);
    }
  };

  const setStep = (step: number) => {
    if (!isControlled) {
      setInternalActiveStep(step);
    }
  }; return (
    <StepperContext.Provider
      value={{
        ...value,
        activeStep: activeStep,
        isError,
        isLoading,
        nextStep,
        prevStep,
        resetSteps,
        setStep,
      }}
    >
      {children}
    </StepperContext.Provider>
  );
};

// <---------- HOOKS ---------->

function useStepper() {
  const context = React.useContext(StepperContext);

  if (context === undefined) {
    throw new Error("useStepper must be used within a StepperProvider");
  }

  // eslint-disable-next-line @typescript-eslint/no-unused-vars, unused-imports/no-unused-vars
  const { children, className, ...rest } = context;

  const isLastStep = context.activeStep === context.steps.length - 1;
  const hasCompletedAllSteps = context.activeStep === context.steps.length;

  const currentStep = context.steps[context.activeStep];
  const isOptionalStep = !!currentStep?.optional;

  const isDisabledStep = context.activeStep === 0;

  return {
    ...rest,
    isLastStep,
    hasCompletedAllSteps,
    isOptionalStep,
    isDisabledStep,
    currentStep,
  };
}

function useMediaQuery(query: string) {
  const [value, setValue] = React.useState(false);

  React.useEffect(() => {
    function onChange(event: MediaQueryListEvent) {
      setValue(event.matches);
    }

    const result = matchMedia(query);
    result.addEventListener("change", onChange);
    setValue(result.matches);

    return () => result.removeEventListener("change", onChange);
  }, [query]);

  return value;
}

// <---------- STEPS ---------->

type StepItem = {
  id?: string;
  label?: string;
  description?: string;
  icon?: IconType;
  optional?: boolean;
};

interface StepOptions {
  orientation?: "vertical" | "horizontal";
  state?: "loading" | "error";
  responsive?: boolean;
  checkIcon?: IconType;
  errorIcon?: IconType;
  onClickStep?: (step: number, setStep: (step: number) => void) => void;
  mobileBreakpoint?: string;
  variant?: "circle" | "circle-alt" | "line";
  expandVerticalSteps?: boolean;
  size?: "sm" | "md" | "lg";
  styles?: {
    /** Styles for the main container */
    "main-container"?: string;
    /** Styles for the horizontal step */
    "horizontal-step"?: string;
    /** Styles for the horizontal step container (button and labels) */
    "horizontal-step-container"?: string;
    /** Styles for the vertical step */
    "vertical-step"?: string;
    /** Styles for the vertical step container (button and labels) */
    "vertical-step-container"?: string;
    /** Styles for the vertical step content */
    "vertical-step-content"?: string;
    /** Styles for the step button container */
    "step-button-container"?: string;
    /** Styles for the label and description container */
    "step-label-container"?: string;
    /** Styles for the step label */
    "step-label"?: string;
    /** Styles for the step description */
    "step-description"?: string;
  };
  variables?: {
    "--step-icon-size"?: string;
    "--step-gap"?: string;
  };
  scrollTracking?: boolean;
}
interface StepperProps extends StepOptions {
  children?: React.ReactNode;
  className?: string;
  initialStep: number;
  activeStep?: number; // External control prop
  steps: StepItem[];
}

const VARIABLE_SIZES = {
  sm: "32px",
  md: "36px",
  lg: "40px",
};

const Stepper = React.forwardRef<HTMLDivElement, StepperProps>((props, ref: React.Ref<HTMLDivElement>) => {
  const {
    className,
    children,
    orientation: orientationProp,
    state,
    responsive,
    checkIcon,
    errorIcon,
    onClickStep,
    mobileBreakpoint,
    expandVerticalSteps = false,
    initialStep = 0,
    activeStep: externalActiveStep,
    size,
    steps,
    variant,
    styles,
    variables,
    scrollTracking = false,
    ...rest
  } = props; const childArr = React.Children.toArray(children).filter(child =>
    React.isValidElement(child)
  );

  const items = [] as React.ReactElement[];

  const footer = childArr.map((child, _index) => {
    // At this point, all children should be valid React elements due to the filter above
    if (child.type === Step) {
      items.push(child);
      return null;
    }

    return child;
  });

  const stepCount = items.length;

  const isMobile = useMediaQuery(
    `(max-width: ${mobileBreakpoint ?? "768px"})`,
  );

  const clickable = !!onClickStep;

  const orientation = isMobile && responsive ? "vertical" : orientationProp;

  const isVertical = orientation === "vertical";

  return (<StepperProvider value={{
    initialStep,
    activeStep: externalActiveStep ?? 0,
    orientation,
    state,
    size,
    responsive,
    checkIcon,
    errorIcon,
    onClickStep,
    clickable,
    stepCount,
    isVertical,
    variant: variant ?? "circle",
    expandVerticalSteps,
    steps,
    scrollTracking,
    styles,
  }}
  >
    <div
      ref={ref}
      className={cn(
        "stepper__main-container",
        "flex w-full flex-wrap",
        stepCount === 1 ? "justify-end" : "justify-between",
        orientation === "vertical" ? "flex-col" : "flex-row",
        variant === "line" && orientation === "horizontal" && "gap-4",
        className,
        styles?.["main-container"],
      )}
      style={
        {
          "--step-icon-size":
            variables?.["--step-icon-size"] ??
            `${VARIABLE_SIZES[size ?? "md"]}`,
          "--step-gap": variables?.["--step-gap"] ?? "8px",
        } as React.CSSProperties
      }
      {...rest}
    >
      <VerticalContent>{items}</VerticalContent>
    </div>
    {orientation === "horizontal" && (
      <HorizontalContent>{items}</HorizontalContent>
    )}
    {footer}
  </StepperProvider>
  );
},
);

const VerticalContent = ({ children }: { children: React.ReactNode }) => {
  const { activeStep } = useStepper();
  const childArr = React.Children.toArray(children); const stepCount = childArr.length;

  return (
    <>
      {React.Children.map(children, (child, i) => {
        let isCompletedStep = i < activeStep;
        let stepChildren = null;
        if (React.isValidElement(child)) {
          const el = child as React.ReactElement<any>;
          isCompletedStep = el.props.isCompletedStep ?? i < activeStep;
          if (i === activeStep) {
            stepChildren = el.props.children;
          }
        }
        const isLastStep = i === stepCount - 1;
        const isCurrentStep = i === activeStep;
        const onlyFirstValidChild = (children: any) => {
          if (Array.isArray(children)) {
            const found = children.find((el) => React.isValidElement(el));
            if (!found) { } else {
              return found;
            }
          }
          return children;
        };
        const stepProps = {
          index: i,
          isCompletedStep,
          isCurrentStep,
          isLastStep,
          children: isCurrentStep ? onlyFirstValidChild(stepChildren) : null,
        };
        if (React.isValidElement(child)) {
          const clonedElement = React.cloneElement(child, stepProps);
          return clonedElement;
        }
        return null;
      })}
    </>
  );
};

const HorizontalContent = ({ children }: { children: React.ReactNode }) => {
  const { activeStep } = useStepper(); const childArr = React.Children.toArray(children);
  if (activeStep > childArr.length) {
    return null;
  }

  return (
    <>
      {React.Children.map(childArr[activeStep], (node) => {
        if (!React.isValidElement(node)) {
          return null;
        }
        // TypeScript: node is ReactElement here
        return React.Children.map(
          (node as React.ReactElement<any>).props.children,
          (childNode) => childNode,
        );
      })}
    </>
  );
};

// <---------- STEP ---------->

interface StepProps extends React.HTMLAttributes<HTMLLIElement> {
  label?: string | React.ReactNode;
  description?: string;
  icon?: IconType;
  state?: "loading" | "error";
  checkIcon?: IconType;
  errorIcon?: IconType;
  isCompletedStep?: boolean;
  isKeepError?: boolean;
  onClickStep?: (step: number, setStep: (step: number) => void) => void;
}

interface StepSharedProps extends StepProps {
  isLastStep?: boolean;
  isCurrentStep?: boolean;
  index?: number;
  hasVisited: boolean | undefined;
  isError?: boolean;
  isLoading?: boolean;
}

// Props which shouldn't be passed to to the Step component from the user
interface StepInternalConfig {
  index: number;
  isCompletedStep?: boolean;
  isCurrentStep?: boolean;
  isLastStep?: boolean;
}

interface FullStepProps extends StepProps, StepInternalConfig { }

const Step = React.forwardRef<HTMLDivElement, StepProps>(
  (props, ref: React.Ref<any>) => {
    const {
      children,
      description,
      icon,
      state,
      checkIcon,
      errorIcon,
      index,
      isCompletedStep,
      isCurrentStep,
      isLastStep,
      isKeepError,
      label,
      onClickStep, } = props as FullStepProps;

    const { isVertical, isError, isLoading, clickable } = useStepper();

    const hasVisited = isCurrentStep ?? isCompletedStep;

    const sharedProps = {
      isLastStep,
      isCompletedStep,
      isCurrentStep,
      index,
      isError,
      isLoading,
      clickable,
      label,
      description,
      hasVisited,
      icon,
      isKeepError,
      checkIcon,
      state,
      errorIcon,
      onClickStep,
    };

    // Only render children if this is the current step
    const stepChildren = isCurrentStep ? children : null;

    const renderStep = () => {
      switch (isVertical) {
        case true:
          return (
            <VerticalStep ref={ref} {...sharedProps}>
              {stepChildren}
            </VerticalStep>
          );
        default:
          return <HorizontalStep ref={ref} {...sharedProps} />;
      }
    };

    return renderStep();
  },
);
Step.displayName = "Step";

// <---------- VERTICAL STEP ---------->

type VerticalStepProps = StepSharedProps & {
  children?: React.ReactNode;
};

const verticalStepVariants = cva(
  "flex flex-col relative transition-all duration-200",
  {
    variants: {
      variant: {
        circle: cn(
          "pb-[var(--step-gap)] gap-[var(--step-gap)]",
          "[&:not(:last-child)]:after:content-[''] [&:not(:last-child)]:after:w-[2px] [&:not(:last-child)]:after:bg-border",
          "[&:not(:last-child)]:after:inset-x-[calc(var(--step-icon-size)/2)]",
          "[&:not(:last-child)]:after:absolute",
          "[&:not(:last-child)]:after:top-[calc(var(--step-icon-size)+var(--step-gap))]",
          "[&:not(:last-child)]:after:bottom-[var(--step-gap)]",
          "[&:not(:last-child)]:after:transition-all [&:not(:last-child)]:after:duration-200",
        ),
        line: "flex-1 border-t-0 mb-4",
      },
    },
  },
);

const VerticalStep = React.forwardRef<HTMLDivElement, VerticalStepProps>(
  (props, ref) => {
    const {
      children,
      index,
      isCompletedStep,
      isCurrentStep,
      label,
      description,
      icon,
      hasVisited,
      state,
      checkIcon: checkIconProp,
      errorIcon: errorIconProp,
      onClickStep,
    } = props;

    const {
      checkIcon: checkIconContext,
      errorIcon: errorIconContext,
      isError,
      isLoading,
      variant,
      onClickStep: onClickStepGeneral,
      clickable,
      expandVerticalSteps,
      styles,
      scrollTracking,
      orientation,
      steps,
      setStep,
    } = useStepper();

    const opacity = hasVisited ? 1 : 0.8;
    const localIsLoading = isLoading ?? state === "loading";
    const localIsError = isError ?? state === "error";

    const active =
      variant === "line" ? isCompletedStep ?? isCurrentStep : isCompletedStep;
    const checkIcon = checkIconProp ?? checkIconContext;
    const errorIcon = errorIconProp ?? errorIconContext;

    const getFirstValidChild = (children: any) => {
      if (Array.isArray(children)) {
        const found = children.find((el) => React.isValidElement(el));
        if (!found) { }
        return found;
      }
      return React.isValidElement(children) ? children : null;
    };
    const renderChildren = () => {
      const childToRender = getFirstValidChild(children);
      if (!expandVerticalSteps) {
        return (
          <Collapsible open={isCurrentStep}>
            <CollapsibleContent className="data-[state=closed]:animate-collapsible-up data-[state=open]:animate-collapsible-down">
              {childToRender}
            </CollapsibleContent>
          </Collapsible>);
      }
      return childToRender;
    };

    return (
      <div
        ref={ref}
        className={cn(
          "stepper__vertical-step",
          verticalStepVariants({
            variant: variant?.includes("circle") ? "circle" : "line",
          }),
          "data-[completed=true]:[&:not(:last-child)]:after:bg-primary",
          "data-[invalid=true]:[&:not(:last-child)]:after:bg-destructive",
          styles?.["vertical-step"],
        )}
        data-optional={steps[index ?? 0]?.optional}
        data-completed={isCompletedStep}
        data-active={active}
        data-clickable={clickable ?? !!onClickStep}
        data-invalid={localIsError}
        onClick={() =>
          onClickStep?.(index ?? 0, setStep) ??
          onClickStepGeneral?.(index ?? 0, setStep)
        }
      >
        <div
          data-vertical={true}
          data-active={active}
          className={cn(
            "stepper__vertical-step-container",
            "flex items-center",
            variant === "line" &&
            "border-s-[3px] py-2 ps-3 data-[active=true]:border-primary",
            styles?.["vertical-step-container"],
          )}
        >
          <StepButtonContainer
            {...{ isLoading: localIsLoading, isError: localIsError, ...props }}
          >
            <StepIcon
              {...{
                index,
                isError: localIsError,
                isLoading: localIsLoading,
                isCurrentStep,
                isCompletedStep,
              }}
              icon={icon}
              checkIcon={checkIcon}
              errorIcon={errorIcon}
            />
          </StepButtonContainer>
          <StepLabel
            label={label}
            description={description}
            {...{ isCurrentStep, opacity }}
          />
        </div>
        <div
          ref={(node) => {
            if (scrollTracking) {
              node?.scrollIntoView({
                behavior: "smooth",
                block: "center",
              });
            }
          }}
          className={cn(
            "stepper__vertical-step-content",
            "min-h-4",
            variant !== "line" && "ps-[--step-icon-size]",
            variant === "line" && orientation === "vertical" && "min-h-0",
            styles?.["vertical-step-content"],
          )}
        >
          {renderChildren()}
        </div>
      </div>
    );
  },
);
VerticalStep.displayName = "VerticalStep";

// <---------- HORIZONTAL STEP ---------->

const HorizontalStep = React.forwardRef<HTMLDivElement, StepSharedProps>(
  (props, ref) => {
    const {
      isError,
      isLoading,
      onClickStep,
      variant,
      clickable,
      checkIcon: checkIconContext,
      errorIcon: errorIconContext,
      styles,
      steps,
      setStep,
    } = useStepper();

    const {
      index,
      isCompletedStep,
      isCurrentStep,
      hasVisited,
      icon,
      label,
      description,
      isKeepError,
      state,
      checkIcon: checkIconProp,
      errorIcon: errorIconProp,
    } = props;

    const localIsLoading = isLoading ?? state === "loading";
    const localIsError = isError ?? state === "error";

    const opacity = hasVisited ? 1 : 0.8;

    const active =
      variant === "line" ? isCompletedStep ?? isCurrentStep : isCompletedStep;

    const checkIcon = checkIconProp ?? checkIconContext;
    const errorIcon = errorIconProp ?? errorIconContext;

    return (
      <div
        aria-disabled={!hasVisited}
        className={cn(
          "stepper__horizontal-step",
          "relative flex items-center transition-all duration-200",
          "[&:not(:last-child)]:flex-1",
          "[&:not(:last-child)]:after:transition-all [&:not(:last-child)]:after:duration-200",
          "[&:not(:last-child)]:after:h-[2px] [&:not(:last-child)]:after:bg-border [&:not(:last-child)]:after:content-['']",
          "data-[completed=true]:[&:not(:last-child)]:after:bg-primary",
          "data-[invalid=true]:[&:not(:last-child)]:after:bg-destructive",
          variant === "circle-alt" &&
          "flex-1 flex-col justify-start [&:not(:last-child)]:after:relative [&:not(:last-child)]:after:end-[50%] [&:not(:last-child)]:after:start-[50%] [&:not(:last-child)]:after:top-[calc(var(--step-icon-size)/2)] [&:not(:last-child)]:after:order-[-1] [&:not(:last-child)]:after:w-[calc((100%-var(--step-icon-size))-(var(--step-gap)))]",
          variant === "circle" &&
          "[&:not(:last-child)]:after:me-[var(--step-gap)] [&:not(:last-child)]:after:ms-[var(--step-gap)] [&:not(:last-child)]:after:flex-1 [&:not(:last-child)]:after:basis-2/4",
          variant === "line" &&
          "flex-1 flex-col border-t-[3px] data-[active=true]:border-primary",
          styles?.["horizontal-step"],
        )}
        data-optional={steps[index ?? 0]?.optional}
        data-completed={isCompletedStep}
        data-active={active}
        data-invalid={localIsError}
        data-clickable={clickable}
        onClick={() => onClickStep?.(index ?? 0, setStep)}
        ref={ref}
      >
        <div
          className={cn(
            "stepper__horizontal-step-container",
            "flex items-center",
            variant === "circle-alt" && "flex-col justify-center gap-1",
            variant === "line" && "w-full",
            styles?.["horizontal-step-container"],
          )}
        >
          <StepButtonContainer
            {...{ ...props, isError: localIsError, isLoading: localIsLoading }}
          >
            <StepIcon
              {...{
                index,
                isCompletedStep,
                isCurrentStep,
                isError: localIsError,
                isKeepError,
                isLoading: localIsLoading,
              }}
              icon={icon}
              checkIcon={checkIcon}
              errorIcon={errorIcon}
            />
          </StepButtonContainer>
          <StepLabel
            label={label}
            description={description}
            {...{ isCurrentStep, opacity }}
          />
        </div>
      </div>
    );
  },
);
HorizontalStep.displayName = "HorizontalStep";

// <---------- STEP BUTTON CONTAINER ---------->

type StepButtonContainerProps = StepSharedProps & {
  children?: React.ReactNode;
};

const StepButtonContainer = ({
  isCurrentStep,
  isCompletedStep,
  children,
  isError,
  isLoading: isLoadingProp,
  onClickStep,
}: StepButtonContainerProps) => {
  const {
    clickable,
    isLoading: isLoadingContext,
    variant,
    styles,
  } = useStepper();

  const currentStepClickable = clickable ?? !!onClickStep;

  const isLoading = isLoadingProp ?? isLoadingContext;

  if (variant === "line") {
    return null;
  }

  return (
    <Button
      variant="ghost"
      className={cn(
        "stepper__step-button-container",
        "pointer-events-none rounded-full p-0",
        "h-[var(--step-icon-size)] min-h-[var(--step-icon-size)] w-[var(--step-icon-size)] min-w-[var(--step-icon-size)]",
        "flex items-center justify-center rounded-full border-2",
        "data-[clickable=true]:pointer-events-auto",
        "data-[active=true]:border-primary data-[active=true]:bg-primary data-[active=true]:text-primary-foreground dark:data-[active=true]:text-primary-foreground",
        "data-[current=true]:border-primary data-[current=true]:bg-secondary",
        "data-[invalid=true]:!border-destructive data-[invalid=true]:!bg-destructive data-[invalid=true]:!text-primary-foreground dark:data-[invalid=true]:!text-foreground",
        styles?.["step-button-container"],
      )}
      aria-current={isCurrentStep ? "step" : undefined}
      data-current={isCurrentStep}
      data-invalid={isError && (isCurrentStep ?? isCompletedStep)}
      data-active={isCompletedStep}
      data-clickable={currentStepClickable}
      data-loading={isLoading && (isCurrentStep ?? isCompletedStep)}
    >
      {children}
    </Button>
  );
};

// <---------- STEP ICON ---------->

type IconType = LucideIcon | React.ComponentType<any> | undefined;

const iconVariants = cva("", {
  variants: {
    size: {
      sm: "size-4",
      md: "size-4",
      lg: "size-5",
    },
  },
  defaultVariants: {
    size: "md",
  },
});

interface StepIconProps {
  isCompletedStep?: boolean;
  isCurrentStep?: boolean;
  isError?: boolean;
  isLoading?: boolean;
  isKeepError?: boolean;
  icon?: IconType;
  index?: number;
  checkIcon?: IconType;
  errorIcon?: IconType;
}

const StepIcon = React.forwardRef<HTMLDivElement, StepIconProps>(
  (props, ref) => {
    const { size } = useStepper();

    const {
      isCompletedStep,
      isCurrentStep,
      isError,
      isLoading,
      isKeepError,
      icon: CustomIcon,
      index,
      checkIcon: CustomCheckIcon,
      errorIcon: CustomErrorIcon,
    } = props;

    const Icon = React.useMemo(
      () => (CustomIcon ? CustomIcon : null),
      [CustomIcon],
    );

    const ErrorIcon = React.useMemo(
      () => (CustomErrorIcon ? CustomErrorIcon : null),
      [CustomErrorIcon],
    );

    const Check = React.useMemo(
      () => (CustomCheckIcon ? CustomCheckIcon : CheckIcon),
      [CustomCheckIcon],
    );

    return React.useMemo(() => {
      if (isCompletedStep) {
        if (isError && isKeepError) {
          return (
            <div key="icon">
              <X className={cn(iconVariants({ size }))} />
            </div>
          );
        }
        return (
          <div key="check-icon">
            <Check className={cn(iconVariants({ size }))} />
          </div>
        );
      }
      if (isCurrentStep) {
        if (isError && ErrorIcon) {
          return (
            <div key="error-icon">
              <ErrorIcon className={cn(iconVariants({ size }))} />
            </div>
          );
        }
        if (isError) {
          return (
            <div key="icon">
              <X className={cn(iconVariants({ size }))} />
            </div>
          );
        }
        if (isLoading) {
          return (
            <Loader2 className={cn(iconVariants({ size }), "animate-spin")} />
          );
        }
      }
      if (Icon) {
        return (
          <div key="step-icon">
            <Icon className={cn(iconVariants({ size }))} />
          </div>
        );
      }
      return (
        <span
          ref={ref}
          key="label"
          className={cn("text-md text-center font-medium")}
        >
          {(index ?? 0) + 1}
        </span>
      );
    }, [
      isCompletedStep,
      isCurrentStep,
      isError,
      isLoading,
      Icon,
      index,
      Check,
      ErrorIcon,
      isKeepError,
      ref,
      size,
    ]);
  },
);
StepIcon.displayName = "StepIcon";

// <---------- STEP LABEL ---------->

interface StepLabelProps {
  isCurrentStep?: boolean;
  opacity: number;
  label?: string | React.ReactNode;
  description?: string | null;
}

const labelVariants = cva("", {
  variants: {
    size: {
      sm: "text-sm",
      md: "text-sm",
      lg: "text-base",
    },
  },
  defaultVariants: {
    size: "md",
  },
});

const descriptionVariants = cva("", {
  variants: {
    size: {
      sm: "text-xs",
      md: "text-xs",
      lg: "text-sm",
    },
  },
  defaultVariants: {
    size: "md",
  },
});

const StepLabel = ({
  isCurrentStep,
  opacity,
  label,
  description,
}: StepLabelProps) => {
  const { variant, styles, size, orientation } = useStepper();
  const shouldRender = !!label || !!description;

  return shouldRender ? (
    <div
      aria-current={isCurrentStep ? "step" : undefined}
      className={cn(
        "stepper__step-label-container text-wrap",
        "flex flex-col",
        variant !== "line" ? "ms-2" : orientation === "horizontal" && "my-2",
        variant === "circle-alt" && "text-center",
        variant === "circle-alt" && orientation === "horizontal" && "ms-0",
        variant === "circle-alt" && orientation === "vertical" && "text-start",
        styles?.["step-label-container"],
      )}
      style={{
        opacity,
      }}
    >
      {!!label && (
        <span
          className={cn(
            "stepper__step-label",
            labelVariants({ size }),
            styles?.["step-label"],
          )}
        >
          {label}
        </span>
      )}
      {!!description && (
        <Tooltip>
          <TooltipTrigger asChild>
            <span
              className={cn(
                "stepper__step-description",
                "whitespace-nowrap",
                "overflow-hidden",
                "text-ellipsis",
                "max-w-28",
                "text-muted-foreground",
                descriptionVariants({ size }),
                styles?.["step-description"],
              )}
            >
              {description}
            </span>
          </TooltipTrigger>
          <TooltipContent side="bottom">{description}</TooltipContent>
        </Tooltip>
      )}
    </div>
  ) : null;
};

export { Step, Stepper, useStepper };
export type { StepItem, StepProps, StepperProps };
