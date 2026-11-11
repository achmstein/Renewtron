import { useMutation } from '@tanstack/react-query'
import { sileo } from 'sileo'

/** A form's submit handler that runs `action` with a "Saving …" / "… saved." toast. */
export function useSave() {
  const saveMutation = useMutation({
    mutationFn: (action: () => Promise<void>) => action(),
  })
  return (label: string, action: () => Promise<void>) => (e: React.FormEvent) => {
    e.preventDefault()
    void sileo.promise(saveMutation.mutateAsync(action), {
      loading: { title: `Saving ${label}…` },
      success: () => ({ title: `${label} saved.` }),
      error: (err) => ({ title: `Error saving ${label}`, description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }
}

/** Value and setter for one section's form. */
export type FormProps<T> = { value: T; onChange: (v: T) => void }
