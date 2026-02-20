"use client";

import { List, LucideSettings, Plug, Sparkles, Library } from "lucide-react";
import Image from "next/image";
import Link from "next/link";

import { DownloadCounters } from "@/components/kzk/layout/download-counters";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

export const sidebarItems = [
  {
    name: "Library",
    href: "/library",
    icon: <AppIc />,
    topSide: true,
  },
  {
    name: "Newly Minted",
    href: "/cloud-latest",
    icon: <Sparkles className="h-6 w-6" />,
    topSide: true,
  },
  {
    name: "Queue",
    href: "/queue",
    icon: <List className="h-6 w-6" />,
    topSide: true,
  },
  {
    name: "Sources",
    href: "/providers",
    icon: <Plug className="h-6 w-6" />,
    topSide: true,
  },
  {
    name: "Settings",
    href: "/settings",
    icon: <LucideSettings className="h-6 w-6" />,
    topSide: true,
  },
];

export default function KzkSidebar() {
  return (
    <aside className="fixed inset-y-0 left-0 z-10 hidden w-14 flex-col border-r bg-background sm:flex">
      <nav className="flex flex-col items-center gap-4 px-2 py-4">
        {sidebarItems
          .filter((item) => item.topSide)
          .map((item, index) => {
            return (
              <Tooltip key={index}>
                <TooltipTrigger asChild>
                  <Link
                    href={item.href}
                    className="flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:scale-110 hover:text-foreground md:h-8 md:w-8"
                  >
                    {item.icon}
                    <span className="sr-only">{item.name}</span>
                  </Link>
                </TooltipTrigger>
                <TooltipContent side="right" className="z-10">{item.name}</TooltipContent>
              </Tooltip>
            );
          })}
      </nav>
      <nav className="mt-auto flex flex-col items-center gap-4 px-2 py-4">        {sidebarItems
          .filter((item) => !item.topSide)
          .map((item, index) => {
            return (
              <Tooltip key={index}>
                <TooltipTrigger asChild>
                  <Link
                    href={item.href}
                    className="flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:scale-110hover:text-foreground md:h-8 md:w-8"
                  >
                    {item.icon}
                    <span className="sr-only">{item.name}</span>
                  </Link>
                </TooltipTrigger>
                <TooltipContent side="right" className="z-10">{item.name}</TooltipContent>
              </Tooltip>
            );
          })}
        
        {/* Download Counters */}
        <DownloadCounters />
      </nav>
    </aside>
  );
}

export function KzkNavbar() {
  return (
    <nav className="grid gap-6 text-lg font-medium">
    {sidebarItems.map((item, index) => {
        return (
          <Link
            key={index}
            href={item.href}
            className="flex items-center gap-4 px-2.5 text-muted-foreground hover:text-foreground"
          >
            {item.icon}
            {item.name}
          </Link>
        );
      })}
      
      {/* Download Counters for mobile */}
      <div className="px-2.5 mt-4">
        <DownloadCounters />
      </div>
    </nav>
  );
}
function AppIc()
{
  return (<Image
        src="/kaizoku.net.png"
        alt="Kaizoku Net"
        priority
        width={200}
        className="h-8 w-8 transition-all group-hover:scale-110"
        height={200}
      />)
}
function AppIcon() {
  return (
    <Link
      href="/library/"
      className="group flex h-8 w-8 shrink-0 items-center justify-center gap-2 text-lg font-semibold text-primary-foreground"
    >
      <Image
        src="/kaizoku.net.png"
        alt="Kaizoku Net"
        priority
        width={200}
        className="h-8 w-8 transition-all group-hover:scale-110"
        height={200}
      />
      <span className="sr-only">Kaizoku</span>
    </Link>
  );
}
