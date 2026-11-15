import { Link } from 'react-router-dom'
import { siteLinks } from './siteLinks'

type FooterLink = { label: string; to?: string; href?: string }

const columns: Array<{ title: string; links: FooterLink[] }> = [
  {
    title: 'Services',
    links: [
      { label: 'Renew Business Name', to: '/' },
      { label: 'Register Business Name', to: '/register' },
      { label: 'Update ASIC Key', to: '/updateasickey' },
      { label: 'Cancel Registration', href: siteLinks.cancel },
    ],
  },
  {
    title: 'Help & Support',
    links: [
      { label: 'About Us', href: siteLinks.about },
      { label: 'Contact Us', href: siteLinks.contact },
      { label: 'News & Guides', href: siteLinks.news },
    ],
  },
  {
    title: 'Legal',
    links: [
      { label: 'Privacy Policy', href: siteLinks.privacy },
      { label: 'Terms of Use', href: siteLinks.terms },
    ],
  },
]

const linkClass = 'text-[14px] text-gray-500 hover:text-gray-900 hover:underline'

function FooterItem({ link }: { link: FooterLink }) {
  return link.to
    ? <Link to={link.to} className={linkClass}>{link.label}</Link>
    : <a href={link.href} className={linkClass}>{link.label}</a>
}

export default function SiteFooter() {
  const year = new Date().getFullYear()
  return (
    <footer className="bg-white border-t border-gray-200 py-10 px-6 mt-auto">
      <div className="max-w-[1200px] mx-auto">
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-[2fr_1fr_1fr_1fr] gap-8 md:gap-10">
          <div>
            <span className="text-base font-bold brand-text">Business Name Services</span>
            <p className="text-[13px] text-gray-500 mt-3 max-w-[280px] leading-relaxed">
              Helping Australian businesses with their registration and compliance needs since 2015.
            </p>
          </div>

          {columns.map((col) => (
            <div key={col.title}>
              <h4 className="font-semibold text-[13px] text-gray-900 mb-4 uppercase tracking-wide">{col.title}</h4>
              <ul className="space-y-2.5">
                {col.links.map((link) => <li key={link.label}><FooterItem link={link} /></li>)}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-8 pt-6 border-t border-gray-200 flex flex-col md:flex-row justify-between items-center gap-4 text-[13px] text-gray-500">
          <span>© {year} Business Name Services. All rights reserved.</span>
          <span className="text-center md:text-right">This is NOT an official ASIC page. We are a separate third party service.</span>
        </div>
      </div>
    </footer>
  )
}
