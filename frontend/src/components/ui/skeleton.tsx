import { cn } from "@/lib/utils"

/** A placeholder block with a light sweeping across it (the .shimmer class in index.css). */
function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      aria-hidden
      className={cn("shimmer rounded-md", className)}
      {...props}
    />
  )
}

export { Skeleton }
