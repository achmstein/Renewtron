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

          <div>
            <h4 className="font-semibold text-[13px] text-gray-900 mb-4 uppercase tracking-wide">Services</h4>
            <ul className="space-y-2.5">
              <li><span className="text-[14px] text-gray-500">Renew Business Name</span></li>
              <li><span className="text-[14px] text-gray-500">Register an ABN</span></li>
              <li><span className="text-[14px] text-gray-500">Register a Company</span></li>
              <li><span className="text-[14px] text-gray-500">Register a Trust</span></li>
            </ul>
          </div>

          <div>
            <h4 className="font-semibold text-[13px] text-gray-900 mb-4 uppercase tracking-wide">Resources</h4>
            <ul className="space-y-2.5">
              <li><span className="text-[14px] text-gray-500">ABN Lookup</span></li>
              <li><span className="text-[14px] text-gray-500">Fee Schedule</span></li>
              <li><span className="text-[14px] text-gray-500">FAQs</span></li>
              <li><span className="text-[14px] text-gray-500">Blog</span></li>
            </ul>
          </div>

          <div>
            <h4 className="font-semibold text-[13px] text-gray-900 mb-4 uppercase tracking-wide">Contact</h4>
            <ul className="space-y-2.5">
              <li><span className="text-[14px] text-gray-500">Help Centre</span></li>
              <li><span className="text-[14px] text-gray-500">Email Support</span></li>
              <li><span className="text-[14px] text-gray-500">Phone Support</span></li>
              <li><span className="text-[14px] text-gray-500">Live Chat</span></li>
            </ul>
          </div>
        </div>

        <div className="mt-8 pt-6 border-t border-gray-200 flex flex-col md:flex-row justify-between items-center gap-4 text-[13px] text-gray-500">
          <span>© {year} Business Name Services. All rights reserved.</span>
          <div className="flex gap-6">
            <span>Privacy Policy</span>
            <span>Terms of Service</span>
            <span>Disclaimer</span>
          </div>
        </div>
      </div>
    </footer>
  )
}
