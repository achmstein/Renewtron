interface Props {
  currentStep: number
  steps: { label: string }[]
  allowNavigation?: boolean
  onStepClick?: (step: number) => void
}

export default function WizardProgress({ currentStep, steps, allowNavigation = true, onStepClick }: Props) {
  const handle = (step: number) => {
    if (allowNavigation && step < currentStep && onStepClick) onStepClick(step)
  }

  return (
    <nav aria-label="Progress" className="wizard-progress">
      <ol role="list" className="wizard-steps">
        {steps.map((step, i) => {
          const stepNumber = i + 1
          const isCompleted = stepNumber < currentStep
          const isCurrent = stepNumber === currentStep
          const isClickable = isCompleted && allowNavigation
          const isLastStep = i === steps.length - 1

          return (
            <li key={step.label + i} className={`wizard-step ${isLastStep ? '' : 'wizard-step-with-line'}`}>
              <div
                className={`wizard-step-content ${isClickable ? 'cursor-pointer' : ''}`}
                onClick={() => handle(stepNumber)}
              >
                {isCompleted ? (
                  <span className="wizard-circle wizard-circle-completed">
                    <svg className="w-4 h-4 text-white" viewBox="0 0 20 20" fill="currentColor">
                      <path fillRule="evenodd" d="M16.704 4.153a.75.75 0 01.143 1.052l-8 10.5a.75.75 0 01-1.127.075l-4.5-4.5a.75.75 0 011.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 011.05-.143z" clipRule="evenodd" />
                    </svg>
                  </span>
                ) : isCurrent ? (
                  <span className="wizard-circle wizard-circle-current">
                    <span className="wizard-circle-dot" />
                  </span>
                ) : (
                  <span className="wizard-circle wizard-circle-upcoming" />
                )}
                <span className={`wizard-label ${isCurrent ? 'wizard-label-current' : isCompleted ? 'wizard-label-completed' : 'wizard-label-upcoming'}`}>
                  {step.label}
                </span>
              </div>

              {!isLastStep ? (
                <div className={`wizard-line ${isCompleted ? 'wizard-line-completed' : 'wizard-line-upcoming'}`} />
              ) : null}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
