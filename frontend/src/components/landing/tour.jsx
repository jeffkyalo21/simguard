import { InView } from "@/components/landing/in-view";
import { ProductTour } from "@/components/landing/product-tour";
import { SectionHeading } from "@/components/landing/section-heading";
import { AlertsScreen, DashboardScreen, ProfileScreen } from "@/components/landing/tour-screens";
import { copy } from "@/lib/copy";

const text = copy.landing.tour;
const screens = { dashboard: <DashboardScreen />, profile: <ProfileScreen />, alerts: <AlertsScreen /> };

/** Product tour: three app screens behind tabs. */
export function Tour() {
  return (
    <InView as="section" aria-labelledby="tour-title" threshold={0.1} className="px-4 pb-24 sm:px-6">
      <div className="mx-auto flex max-w-5xl flex-col gap-8">
        <SectionHeading id="tour-title" title={text.title} intro={text.intro} />
        <ProductTour label={text.label} tabs={text.tabs.map((tab) => ({ ...tab, screen: screens[tab.key] }))} />
      </div>
    </InView>
  );
}
