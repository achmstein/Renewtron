const inputBase =
  'block w-full rounded-md border-0 py-2.5 px-3 text-gray-900 shadow-sm ring-1 ring-inset placeholder:text-gray-400 focus:ring-2 focus:ring-inset sm:text-sm'
export const inputClass = (err?: string) => `${inputBase} ${err ? 'ring-red-300 focus:ring-red-500' : 'ring-gray-300 focus-ring-brand'}`
