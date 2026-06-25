import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ActionMenuContext, type ActionMenuChoice, type ActionMenuRequest, type OpenActionMenu } from './actionMenuContext'

type ActiveActionMenu = ActionMenuRequest<ActionMenuChoice>

export function ActionMenuProvider({ children }: { children: ReactNode }) {
  const [activeMenu, setActiveMenu] = useState<ActiveActionMenu | null>(null)

  function closeActionMenu() {
    setActiveMenu(null)
  }

  const openActionMenu: OpenActionMenu = (request) => {
    setActiveMenu({
      title: request.title,
      choices: request.choices,
      onChoose: (choice) => request.onChoose(choice as (typeof request.choices)[number]),
    })
  }

  const value = useMemo(() => ({ closeActionMenu, openActionMenu }), [])

  useEffect(() => {
    if (!activeMenu) return

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') closeActionMenu()
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [activeMenu])

  return (
    <ActionMenuContext.Provider value={value}>
      {children}
      {activeMenu && createPortal(
        <div className="app-action-menu-backdrop" onClick={closeActionMenu}>
          <section
            aria-label={`${activeMenu.title} actions`}
            className="app-action-menu"
            onClick={(event) => event.stopPropagation()}
          >
            <header>
              <span>Choose action</span>
              <h3>{activeMenu.title}</h3>
            </header>
            <div className="app-action-menu-options">
              {activeMenu.choices.map((choice) => (
                <button
                  key={choice.key}
                  type="button"
                  onClick={() => {
                    closeActionMenu()
                    activeMenu.onChoose(choice)
                  }}
                >
                  {choice.label}
                </button>
              ))}
            </div>
            <footer>
              <button type="button" onClick={closeActionMenu}>Cancel</button>
            </footer>
          </section>
        </div>,
        document.body,
      )}
    </ActionMenuContext.Provider>
  )
}
