import { createContext, useContext } from 'react'

export type ActionMenuChoice = {
  key: string
  label: string
}

export type ActionMenuRequest<TChoice extends ActionMenuChoice = ActionMenuChoice> = {
  title: string
  choices: TChoice[]
  onChoose: (choice: TChoice) => void
}

export type OpenActionMenu = <TChoice extends ActionMenuChoice>(request: ActionMenuRequest<TChoice>) => void

export type ActionMenuContextValue = {
  closeActionMenu: () => void
  openActionMenu: OpenActionMenu
}

export const ActionMenuContext = createContext<ActionMenuContextValue | null>(null)

export function useActionMenu() {
  const context = useContext(ActionMenuContext)
  if (!context) {
    throw new Error('useActionMenu must be used within ActionMenuProvider.')
  }

  return context
}
