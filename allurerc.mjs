import { defineConfig } from "allure";

export default defineConfig({
  name: "API E2E",
  plugins: {
    awesome: {
      import: "@allurereport/plugin-awesome",
      options: {
        reportName: "API E2E · HTTP, SQL Server e Redis",
        reportLanguage: "en",
        groupBy: ["epic", "feature", "story"],
        singleFile: false,
      },
    },
  },
});
