import { useEffect, useState, type ReactNode } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  Archive, ChartColumn, Contact, Copy, CreditCard, FileWarning, Filter, House, Inbox, KeyRound, LogOut,
  Landmark, Mail, RefreshCw, Search, Settings, ShoppingCart, SquarePen, Users, Wrench, FilePlus2,
} from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { api, type SidebarCounts } from '../api/client'
import { ClientSearch, ClientSearchButton } from './ClientSearch'
import { Wordmark } from '@/components/Wordmark'
import {
  Sidebar, SidebarContent, SidebarFooter, SidebarGroup, SidebarGroupLabel, SidebarHeader, SidebarInset, SidebarMenu,
  SidebarMenuBadge, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger, useSidebar,
} from '@/components/ui/sidebar'

/**
 * One sidebar entry. `to` may carry a query (`?view=to-fix`) for a queue that is a view of a list
 * page; the badge names a count from /api/admin/sidebar-counts (work waiting on staff).
 */
interface NavItem { to: string; label: string; icon: ReactNode; badge?: keyof SidebarCounts }
interface NavSection { kicker: string; items: NavItem[] }

const Icon = {
  Today: <House />,
  Dashboard: <ChartColumn />,
  Search: <Search />,
  Refresh: <RefreshCw />,
  Wrench: <Wrench />,
  Users: <Users />,
  Contact: <Contact />,
  Copy: <Copy />,
  Cart: <ShoppingCart />,
  Card: <CreditCard />,
  Bank: <Landmark />,
  Bulk: <Archive />,
  Pencil: <SquarePen />,
  Key: <KeyRound />,
  KeyAlert: <FileWarning />,
  Inbox: <Inbox />,
  Mail: <Mail />,
  NewName: <FilePlus2 />,
  Funnel: <Filter />,
  Cog: <Settings />,
  Logout: <LogOut />,
}

// Grouped by job: the work waiting on a person first, then who it is for, money, numbers,
// and last the raw records for digging.
const navSections: NavSection[] = [
  {
    kicker: 'Work',
    items: [
      { to: '/admin', label: 'Today', icon: Icon.Today, badge: 'today' },
      { to: '/admin/renewal-orders?view=to-fix', label: 'Renewals to fix', icon: Icon.Wrench, badge: 'renewalsToFix' },
      { to: '/admin/bpay', label: 'BPAY to pay', icon: Icon.Bank, badge: 'bpayToPay' },
      { to: '/admin/registrations', label: 'New names', icon: Icon.NewName, badge: 'registrations' },
      { to: '/admin/asic-key-requests', label: 'Key requests', icon: Icon.Inbox, badge: 'asicKeyRequests' },
      { to: '/admin/asic-keys?view=to-sort', label: 'Keys to sort', icon: Icon.KeyAlert, badge: 'keysToSort' },
      { to: '/admin/manual-renewal', label: 'Manual renewal', icon: Icon.Pencil },
    ],
  },
  {
    kicker: 'Clients',
    items: [
      { to: '/admin/clients', label: 'All clients', icon: Icon.Contact },
      { to: '/admin/clients/duplicates', label: 'Duplicates', icon: Icon.Copy, badge: 'clientDuplicates' },
    ],
  },
  {
    kicker: 'Money',
    items: [
      { to: '/admin/payments', label: 'Payments', icon: Icon.Card },
    ],
  },
  {
    kicker: 'Reports',
    items: [
      { to: '/admin/overview', label: 'Overview', icon: Icon.Dashboard },
      { to: '/admin/funnel', label: 'Funnel', icon: Icon.Funnel },
    ],
  },
  {
    kicker: 'Records',
    items: [
      { to: '/admin/leads', label: 'Leads', icon: Icon.Users },
      { to: '/admin/searches', label: 'Searches', icon: Icon.Search },
      { to: '/admin/renewals', label: 'Renewals', icon: Icon.Refresh },
      { to: '/admin/ontraport-sales', label: 'Ontraport sales', icon: Icon.Cart },
      { to: '/admin/bulk-renewals', label: 'Bulk uploads', icon: Icon.Bulk },
      { to: '/admin/asic-keys', label: 'Key letters', icon: Icon.Key },
      { to: '/admin/emails', label: 'Emails', icon: Icon.Mail },
    ],
  },
  {
    kicker: 'System',
    items: [
      { to: '/admin/settings', label: 'Settings', icon: Icon.Cog },
    ],
  },
]

const allItems = navSections.flatMap((s) => s.items)

/**
 * The one entry to highlight: the longest path containing the current one (a client's page
 * lights "All clients", the duplicates page lights "Duplicates"), and among those the entry
 * whose `view` matches ("Renewals to fix" vs "Renewals").
 */
function activeItem(pathname: string, search: string): NavItem | undefined {
  const view = new URLSearchParams(search).get('view')
  let best: NavItem | undefined
  let bestLen = -1
  for (const item of allItems) {
    const [path, query] = item.to.split('?')
    const within = path === '/admin'
      ? pathname === '/admin' || pathname === '/admin/'
      : pathname === path || pathname.startsWith(path + '/')
    if (!within) continue
    if (new URLSearchParams(query ?? '').get('view') !== view) continue
    if (path.length > bestLen) { best = item; bestLen = path.length }
  }
  return best
}

function AdminSidebar({ onSearch }: { onSearch: () => void }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { isMobile, setOpenMobile } = useSidebar()
  const active = activeItem(location.pathname, location.search)
  const { data: counts } = useQuery({
    queryKey: ['admin-sidebar-counts'],
    queryFn: api.admin.sidebarCounts,
    refetchInterval: 60_000,
  })
  const close = () => { if (isMobile) setOpenMobile(false) }

  const onSignOut = async () => {
    await logout()
    close()
    navigate('/login')
  }

  return (
    <Sidebar collapsible="icon" className="border-sidebar-border">
      <SidebarHeader className="gap-3 px-4 pt-5 group-data-[collapsible=icon]:px-2">
        <Link to="/admin" onClick={close} className="flex items-center gap-2 px-1 group-data-[collapsible=icon]:hidden">
          <Wordmark tone="dark" className="text-sm" />
        </Link>
        <div className="group-data-[collapsible=icon]:hidden">
          <ClientSearchButton onClick={() => { close(); onSearch() }} />
        </div>
        <SidebarMenu className="hidden group-data-[collapsible=icon]:flex">
          <SidebarMenuItem>
            <SidebarMenuButton tooltip="Find a client" onClick={onSearch} className="text-sidebar-foreground hover:text-sidebar-accent-foreground [&>svg]:text-muted-foreground">
              <Search />
              <span>Find a client</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

      <SidebarContent className="scrollbar-dark px-2 group-data-[collapsible=icon]:px-0">
        {navSections.map((section) => (
          <SidebarGroup key={section.kicker} className="py-1.5">
            <SidebarGroupLabel className="text-muted-foreground">{section.kicker}</SidebarGroupLabel>
            <SidebarMenu>
              {section.items.map((item) => (
                <SidebarMenuItem key={item.to}>
                  <SidebarMenuButton asChild isActive={item === active} tooltip={item.badge && counts?.[item.badge] ? `${item.label} (${counts[item.badge]})` : item.label} className="text-sidebar-foreground hover:text-sidebar-accent-foreground data-[active=true]:font-medium [&>svg]:text-muted-foreground data-[active=true]:[&>svg]:text-brand-300">
                    <Link to={item.to} onClick={close}>
                      {item.icon}
                      <span>{item.label}</span>
                    </Link>
                  </SidebarMenuButton>
                  {item.badge && counts?.[item.badge] ? (
                    <SidebarMenuBadge className="text-brand-300 peer-data-[active=true]/menu-button:text-brand-300">
                      {counts[item.badge]}
                    </SidebarMenuBadge>
                  ) : null}
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroup>
        ))}
      </SidebarContent>

      <SidebarFooter className="border-t border-sidebar-border p-3">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton onClick={onSignOut} tooltip="Sign out" className="h-auto py-2 text-sidebar-foreground hover:text-sidebar-accent-foreground" title="Sign out">
              <LogOut className="text-muted-foreground" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm text-zinc-200">{user?.email ?? 'Signed in'}</span>
                <span className="block text-xs text-muted-foreground">Sign out</span>
              </span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
    </Sidebar>
  )
}

export default function AdminLayout() {
  const [searchOpen, setSearchOpen] = useState(false)
  const location = useLocation()
  // The Emails page is a full-screen mail client: the nav folds to an icon rail to give it room.
  const onEmails = location.pathname === '/admin/emails' || location.pathname.startsWith('/admin/emails/')
  // Fade in on a new page, but not when only the open email changes (that would rebuild the list).
  const pageKey = onEmails ? '/admin/emails' : location.pathname

  // The admin theme (index.css) hangs off <html>, so dialogs and menus portalled into <body> get it too.
  useEffect(() => {
    document.documentElement.classList.add('admin')
    return () => document.documentElement.classList.remove('admin')
  }, [])

  return (
    <SidebarProvider open={!onEmails} onOpenChange={() => {}}>
      <ClientSearch open={searchOpen} setOpen={setSearchOpen} />
      <AdminSidebar onSearch={() => setSearchOpen(true)} />
      <SidebarInset className="min-w-0">
        <header className="sticky top-0 z-40 flex h-12 items-center gap-3 border-b bg-sidebar px-4 md:hidden">
          <SidebarTrigger className="text-muted-foreground/70 hover:bg-card/10 hover:text-white" />
          <Wordmark tone="dark" className="text-sm" />
        </header>
        <div key={pageKey} className="fade-in">
          <Outlet />
        </div>
      </SidebarInset>
    </SidebarProvider>
  )
}
