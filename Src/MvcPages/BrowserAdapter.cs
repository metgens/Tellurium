﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Internal;
using OpenQA.Selenium.Remote;
using Tellurium.MvcPages.BrowserCamera;
using Tellurium.MvcPages.BrowserCamera.Storage;
using Tellurium.MvcPages.Configuration;
using Tellurium.MvcPages.EndpointCoverage;
using Tellurium.MvcPages.Reports.ErrorReport;
using Tellurium.MvcPages.SeleniumUtils;
using Tellurium.MvcPages.SeleniumUtils.Exceptions;
using Tellurium.MvcPages.Utils;
using Tellurium.MvcPages.WebPages;
using Tellurium.MvcPages.WebPages.WebForms;

namespace Tellurium.MvcPages
{
    public class BrowserAdapter : IBrowserAdapter
    {
        private RemoteWebDriver Driver { get; set; }
        private IBrowserCamera browserCamera;
        private IBrowserCamera errorBrowserCamera;

        private INavigator navigator;
        private List<IFormInputAdapter> supportedInputsAdapters;
        public string BrowserName { get; private set; }
        private int NumberOfInputSetRetries { get; set; }
        private AfterFieldValueSet AfterFieldValueSetAction { get; set; }
        private TelluriumErrorReportBuilder errorReportBuilder;
        private EndpointCoverageReportBuilder endpointCoverageReportBuilder;
        private IScreenshotStorage errorScreenshotStorage;

        public BrowserAdapter()
        {
            BrowserAdapterContext.Current = this;
        }

        /// <summary>
        /// Create BrowserAdapter based on configuration
        /// </summary>
        /// <param name="config">BrowserAdapter configuration</param>
        /// <param name="driver">Existing driver, if null the driver will be created using provided configuration</param>
        public static BrowserAdapter Create(BrowserAdapterConfig config, RemoteWebDriver driver = null)
        {
            var browserAdapter = new BrowserAdapter();
            browserAdapter.Driver = driver ?? SeleniumDriverFactory.CreateDriver(config);
            var browserCameraConfig = config.BrowserCameraConfig ?? BrowserCameraConfig.CreateDefault();
            browserAdapter.browserCamera = BrowserCameraFactory.CreateNew(browserAdapter.Driver, browserCameraConfig);
            browserAdapter.errorBrowserCamera = BrowserCameraFactory.CreateErrorBrowserCamera(browserAdapter.Driver);
            var navigator = new Navigator(browserAdapter.Driver, config.PageUrl, config.MeasureEndpointCoverage);
            browserAdapter.navigator = navigator;
            browserAdapter.supportedInputsAdapters = config.InputAdapters.ToList();
            browserAdapter.SetupBrowserDimensions(config.BrowserDimensions);
            browserAdapter.BrowserName = config.BrowserType.ToString();
            browserAdapter.NumberOfInputSetRetries = config.NumberOfInputSetRetries;
            browserAdapter.AfterFieldValueSetAction = config.AfterFieldValueSetAction;
            browserAdapter.errorReportBuilder = TelluriumErrorReportBuilderFactory.Create(config);
            browserAdapter.endpointCoverageReportBuilder = EndpointCoverageReportBuilderFactory.Create(config, navigator);
            browserAdapter.errorScreenshotStorage = ScreenshotStorageFactory.CreateForErrorScreenshot(config);
            if (config.AnimationsDisabled)
            {
                browserAdapter.navigator.PageReload += (sender, args) => browserAdapter.Driver.DisableAnimations();
            }
            return browserAdapter;
        }

        public static void Execute(BrowserAdapterConfig config, Action<BrowserAdapter> action)
        {
            using (var browserAdapter = Create(config))
            {
               browserAdapter.Execute(action);
            }
        }


        public void Execute(Action<BrowserAdapter> action)
        {
            try
            {
                action(this);
            }
            catch (Exception ex)
            {
                this.ReportError(ex);
                throw;
            }
        }

        public  void SetupBrowserDimensions(BrowserDimensionsConfig dimensionsConfig)
        {
            var browserOptions = this.Driver.Manage();
            if (dimensionsConfig == null)
            {
                browserOptions.Window.Maximize();
            }
            else
            {
                browserOptions.Window.Size = new Size
                {
                    Width = dimensionsConfig.Width,
                    Height = dimensionsConfig.Height
                };
            }
        }

        public void NavigateTo<TController>(Expression<Action<TController>> action)
        {
            navigator.NavigateTo(action);
        }

        public void NavigateTo(string subpagePath)
        {
            navigator.NavigateTo(subpagePath);
        }

        public byte [] TakeScreenshot()
        {
            return browserCamera.TakeScreenshot();
        }

        public void SaveScreenshot(string directoryPath, string screenshotName, bool addBrowserPrefix=true)
        {
            var storage = new FileSystemScreenshotStorage(directoryPath);
            var screenshotRawData = browserCamera.TakeScreenshot();
            var fullScreenshotName = addBrowserPrefix ? $"{BrowserName}_{screenshotName}" : screenshotName;
            storage.Persist(screenshotRawData, fullScreenshotName);
        }

        public void ReportError(Exception exception)
        {
            string screenshotName = $"{BrowserName}_Error{DateTime.Now:yyyy_MM_dd__HH_mm_ss}";
            var screenshotRawData = errorBrowserCamera.TakeScreenshot();
           
            if (screenshotRawData != null)
            {
                errorScreenshotStorage?.Persist(screenshotRawData, screenshotName);
                errorReportBuilder.ReportException(exception, screenshotRawData, screenshotName, this.Driver.Url);
            }
            else
            {
                errorReportBuilder.ReportException(exception, this.Driver.Url);
            }
        }

        public  MvcWebForm<TModel> GetForm<TModel>(string formId)
        {
            var formElement = this.Driver.GetStableAccessibleElementById(formId);
            return new MvcWebForm<TModel>(formElement, Driver, supportedInputsAdapters, this.NumberOfInputSetRetries, this.AfterFieldValueSetAction);
        }

        public WebForm GetForm(string formId)
        {
            var formElement = this.Driver.GetStableAccessibleElementById(formId);
            return new WebForm(formElement, Driver, supportedInputsAdapters, this.NumberOfInputSetRetries, this.AfterFieldValueSetAction);
        }

        public  void ClickOn(string elementId)
        {
            var elementToClick = this.Driver.GetStableAccessibleElementById(elementId);
            Driver.ClickOn(elementToClick);
        }

        public void HoverOn(string elementId)
        {
            var elementToHover = this.Driver.GetStableAccessibleElementById(elementId);
            Driver.HoverOn(elementToHover);
        }

        public IPageFragment GetPageFragmentById(string elementId)
        {
            var pageFragment = Driver.GetStableAccessibleElementById(elementId);
            return new PageFragment(Driver, pageFragment);
        }

        public void RefreshPage()
        {
            this.navigator.RefreshPage();
        }
        
        public void WaitForElementWithId(string elementId, int timeOut = 30)
        {
            Driver.GetStableAccessibleElementById(elementId, timeOut);
        }

        public void Wait(int seconds)
        {
            Thread.Sleep(seconds * 1000);
        }

        public PageFragmentWatcher WatchForContentChange(string containerId, bool watchSubtree=true)
        {
            return Driver.WatchForContentChanges(containerId, watchSubtree);
        }

        public void AffectElementWith(string elementId, Action action, bool watchSubtree=true)
        {
            var watcher = WatchForContentChange(elementId, watchSubtree);
            action();
            watcher.WaitForChange();
        }

        public void AffectWith(Action action, bool watchSubtree=true)
        {
            var body = GetPageBody();
            var watcher = new PageFragmentWatcher(Driver, body);
            watcher.StartWatching(watchSubtree);
            action();
            watcher.WaitForChange();
        }

        public IPageFragment GetParent()
        {
            return null;
        }

        public IPageFragment GetElementWithText(string text)
        {
            return GetElementWithText(text, false);
        }

        public IPageFragment GetElementWithPartialText(string text)
        {
            return GetElementWithText(text, true);
        }

        private IPageFragment GetElementWithText(string text, bool isPartialText)
        {
            var element = this.Driver.GetStableElementWithText(this.Driver, text, isPartialText);
            return new PageFragment(this.Driver, element);
        }

        public void ReloadPageWith(Action action)
        {
            MarkPageAsVisited();
            navigator.OnBeforePageReload();
            action();
            try
            {
                Driver.WaitUntil(SeleniumExtensions.PageLoadTimeout, driver => ExceptionHelper.SwallowException(IsNewlyLoadedPage, false));
                navigator.OnPageReload();
            }
            catch (WebDriverTimeoutException)
            {
                throw new CannotReloadPageWithException();
            }
        }

        private bool IsNewlyLoadedPage()
        {
            return Driver.IsPageLoaded() && HasVisitedPageMark() == false;
        }

        private bool HasVisitedPageMark()
        {
            return (bool) Driver.ExecuteScript("return window.__selenium_visited__ === true;");
        }

        private void MarkPageAsVisited()
        {
            Driver.ExecuteScript("window.__selenium_visited__ = true;");
        }

        public void DisableAnimations()
        {
           this.Driver.DisableAnimations();
        }

        public void EnableAnimations()
        {
            this.Driver.EnableAnimations();
        }

        public void Dispose()
        {
            endpointCoverageReportBuilder?.GenerateEndpointCoverageReport();
            BrowserAdapterContext.Current = null;
            Driver.Close();
            Driver.Quit();
        }

        public void Click()
        {
            var body = this.GetPageBody();
            this.Driver.ClickOn(body);
        }

        public void ClickOnElementWithText(string text)
        {
            Driver.ClickOnElementWithText(Driver, text, false);
        }

        public void ClickOnElementWithPartialText(string text)
        {
            Driver.ClickOnElementWithText(Driver, text, true);
        }

        public void Hover()
        {
            var body = this.GetPageBody();
            this.Driver.HoverOn(body);
        }

        public void HoverOnElementWithText(string text)
        {
            Driver.HoverOnElementWithText(Driver, text, false);
        }

        public void HoverOnElementWithPartialText(string text)
        {
            Driver.HoverOnElementWithText(Driver, text, true);
        }

        public WebList GetListWithId(string id)
        {
            return Driver.GetListWithId(id);
        }

        public WebList ToWebList()
        {
            var body = GetPageBody();
            var mainPageFragment = new PageFragment(Driver, body);
            return mainPageFragment.ToWebList();
        }

        public WebTree GetTreeWithId(string id, WebTreeOptions options = null)
        {
            return Driver.GetTreeWithId(id, options);
        }

        public WebTree ToWebTree(WebTreeOptions options=null)
        {
            var body = GetPageBody();
            return new WebTree(Driver, body, options);
        }

        private IWebElement GetPageBody()
        {
            return Driver.FindElementByTagName("body");
        }

        public WebTable GetTableWithId(string id)
        {
            return Driver.GetTableWithId(id);
        }

        public WebTable ToWebTable()
        {
            var body = GetPageBody();
            return new WebTable(Driver, body);
        }

        public void AcceptAlert()
        {
            Driver.SwitchTo().Alert().Accept();
        }

        public string Text => GetPageBody().Text;

        public IWebDriver WrappedDriver => Driver;
        public IWebElement WrappedElement => GetPageBody();

        public void Reset()
        {
            this.Driver.Manage().Cookies.DeleteAllCookies();
            this.Driver.WebStorage.LocalStorage.Clear();
            this.Driver.WebStorage.SessionStorage.Clear();
        }
    }

    public interface IBrowserAdapter : IPageFragment, IBrowserCamera,  IDisposable, IWrapsDriver
    {
        /// <summary>
        /// Return strongly typed adapter for web form with given id
        /// </summary>
        /// <typeparam name="TModel">Model connected with form</typeparam>
        /// <param name="formId">Id of expected form</param>
        MvcWebForm<TModel> GetForm<TModel>(string formId);


        /// <summary>
        /// Return weakly typed adapter for web form with given id
        /// </summary>
        /// <param name="formId">Id of expected form</param>
        WebForm GetForm(string formId);

        /// <summary>
        /// Refresh page
        /// </summary>
        void RefreshPage();

        /// <summary>
        /// Simulate click event on element with given id
        /// </summary>
        /// <param name="elementId">Id of expected element</param>
        void ClickOn(string elementId);


        /// <summary>
        /// Simulate hover event on element with given id
        /// </summary>
        /// <param name="elementId">Id of expected element</param>
        void HoverOn(string elementId);

        /// <summary>
        /// Return page fragment with given id
        /// </summary>
        /// <param name="elementId">Id of expected element</param>
        IPageFragment GetPageFragmentById(string elementId);

        /// <summary>
        /// Stop execution until element with given id appear
        /// </summary>
        /// <param name="elementId">Id of expected element</param>
        /// <param name="timeOut">Max time in seconds to wait</param>
        void WaitForElementWithId(string elementId, int timeOut = 30);

        /// <summary>
        /// Navigate to page represented by given controller's action
        /// </summary>
        /// <param name="action">Expression to given action></param>
        void NavigateTo<TController>(Expression<Action<TController>> action);

        /// <summary>
        /// Navigate to page related to root page
        /// </summary>
        /// <param name="subpagePath">Path to page</param>
        void NavigateTo(string subpagePath);

        void SaveScreenshot(string directoryPath, string screenshotName, bool addBrowserPrefix=true);

        /// <summary>
        /// Wait explicitly given amount of seconds
        /// </summary>
        /// <param name="seconds"></param>
        void Wait(int seconds);

        /// <summary>
        /// Start obserwing container with given id for contnet change
        /// </summary>
        /// <param name="containerId">Container id</param>
        /// <param name="watchSubtree">Set true if changes in subtree shuld also be observed</param>
        PageFragmentWatcher WatchForContentChange(string containerId, bool watchSubtree=true);

        /// <summary>
        /// Perform action and wait until element with given id will change.
        /// </summary>
        /// <param name="elementId">Id of observed element</param>
        /// <param name="action">Action that should have impact on observed element</param>
        /// <param name="watchSubtree">Set true if changes in subtree shuld also be observed</param>
        void AffectElementWith(string elementId, Action action, bool watchSubtree=true);

        /// <summary>
        /// Perform action and wait until page will reaload
        /// </summary>
        /// <param name="action">Action that should cause page reload</param>
        void ReloadPageWith(Action action);

        /// <summary>
        /// Disable animations on page
        /// </summary>
        void DisableAnimations();

        /// <summary>
        /// Restore animations on page
        /// </summary>
        void EnableAnimations();

        /// <summary>
        /// Accept alert dialog
        /// </summary>
        void AcceptAlert();

        /// <summary>
        /// Reset browser state and go back to home page
        /// </summary>
        void Reset();

        /// <summary>
        /// Add exception to errors report
        /// </summary>
        /// <param name="exception"></param>
        void ReportError(Exception exception);
    }
}