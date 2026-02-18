"use client";
import KzkBreadcrumb from "@/components/kzk/layout/breadcrumb";
import { KzkNavbar } from "@/components/kzk/layout/sidebar";
import { Button } from "@/components/ui/button";

import { ThemeSwitcher } from "@/components/theme/theme-switcher";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetTrigger } from "@/components/ui/sheet";
import { useSearch } from "@/contexts/search-context";
import { PanelLeft, Search } from "lucide-react";

interface KzkHeaderProps {
  seriesTitle?: string;
}

export default function KzkHeader({ seriesTitle }: KzkHeaderProps = {}) {
  const { searchTerm, setSearchTerm, currentPage, isSearchDisabled } = useSearch();

  // Get placeholder text based on current page
  const getPlaceholder = () => {
    switch (currentPage) {
      case 'library':
        return 'Search series...';
      case 'providers':
        return 'Search sources...';
      case 'queue':
        return 'Search queue...';
      default:
        return 'Search...';
    }
  };

  return (
    <header className="sticky top-0 z-30 flex h-12 sm:h-14 items-center gap-2 sm:gap-4 border-b bg-background px-2 sm:px-4 sm:static sm:h-auto sm:border-0 sm:bg-transparent sm:px-6">
      <Sheet>
        <SheetTrigger asChild>
          <Button size="icon" variant="outline" className="sm:hidden h-9 w-9 min-h-[36px] min-w-[36px]">
            <PanelLeft className="h-5 w-5" />
            <span className="sr-only">Toggle Menu</span>
          </Button>
        </SheetTrigger>
        <SheetContent side="left" className="max-w-xs">
          <KzkNavbar />
        </SheetContent>
      </Sheet>

      <KzkBreadcrumb seriesTitle={seriesTitle} />

      <div className="relative ml-auto flex-1 md:grow-0">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          type="search"
          placeholder={getPlaceholder()}
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          disabled={isSearchDisabled}
          className="w-full rounded-lg bg-background pl-8 md:w-[200px] lg:w-[320px]"
        />
      </div>
      
      <ThemeSwitcher />
    </header>
  );
}
