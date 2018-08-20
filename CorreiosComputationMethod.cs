﻿using System;
using System.Globalization;
using Nop.Core;
using Nop.Core.Domain.Shipping;
using Nop.Core.Plugins;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Tracking;
using NopBrasil.Plugin.Shipping.Correios.Domain;
using NopBrasil.Plugin.Shipping.Correios.Service;

namespace NopBrasil.Plugin.Shipping.Correios
{
    public class CorreiosComputationMethod : BasePlugin, IShippingRateComputationMethod
    {
        private readonly ISettingService _settingService;
        private readonly CorreiosSettings _correiosSettings;
        private readonly ILogger _logger;
        private readonly IWebHelper _webHelper;
        private readonly ILocalizationService _localizationService;
        private readonly ICorreiosService _correiosService;

        public CorreiosComputationMethod(ISettingService settingService,
            CorreiosSettings correiosSettings, ILogger logger, IWebHelper webHelper,
            ILocalizationService localizationService, ICorreiosService correiosService)
        {
            this._settingService = settingService;
            this._correiosSettings = correiosSettings;
            this._logger = logger;
            this._webHelper = webHelper;
            this._localizationService = localizationService;
            this._correiosService = correiosService;
        }

        private bool ValidateRequest(GetShippingOptionRequest getShippingOptionRequest, GetShippingOptionResponse response)
        {
            if (getShippingOptionRequest.Items == null)
                response.AddError(_localizationService.GetResource("Plugins.Shipping.Correios.Message.NoShipmentItems"));
            if (getShippingOptionRequest.ShippingAddress == null)
                response.AddError(_localizationService.GetResource("Plugins.Shipping.Correios.Message.AddressNotSet"));
            if (getShippingOptionRequest.ShippingAddress.Country == null)
                response.AddError(_localizationService.GetResource("Plugins.Shipping.Correios.Message.CountryNotSet"));
            if (getShippingOptionRequest.ShippingAddress.StateProvince == null)
                response.AddError(_localizationService.GetResource("Plugins.Shipping.Correios.Message.StateNotSet"));
            if (getShippingOptionRequest.ShippingAddress.ZipPostalCode == null)
                response.AddError(_localizationService.GetResource("Plugins.Shipping.Correios.Message.PostalCodeNotSet"));
            return response.Errors.Count > 0 ? false : true;
        }

        public GetShippingOptionResponse GetShippingOptions(GetShippingOptionRequest getShippingOptionRequest)
        {
            if (getShippingOptionRequest == null)
                throw new ArgumentNullException("getShippingOptionRequest");

            var response = new GetShippingOptionResponse();

            if (!ValidateRequest(getShippingOptionRequest, response))
                return response;

            try
            {
                WSCorreiosCalcPrecoPrazo.cResultado wsResult = _correiosService.RequestCorreios(getShippingOptionRequest);
                foreach (WSCorreiosCalcPrecoPrazo.cServico serv in wsResult?.Servicos)
                {
                    try
                    {
                        var obs = ValidateWSResult(serv);
                        response.ShippingOptions.Add(GetShippingOption(ApplyAdditionalFee(Convert.ToDecimal(serv.Valor, new CultureInfo("pt-BR"))), CorreiosServiceType.GetServiceName(serv.Codigo.ToString()), CalcPrazoEntrega(serv), obs));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e.Message, e);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e.Message, e);
            }

            if (response.ShippingOptions.Count <= 0)
                response.ShippingOptions.Add(GetShippingOption(_correiosSettings.ShippingRateDefault, _correiosSettings.ServiceNameDefault, _correiosSettings.QtdDaysForDeliveryDefault));

            return response;
        }

        private decimal ApplyAdditionalFee(decimal rate) => _correiosSettings.PercentageShippingFee > 0.0M ? rate * _correiosSettings.PercentageShippingFee : rate;

        private ShippingOption GetShippingOption(decimal rate, string serviceName, int prazo, string obs = null)
        {
            var shippingName = $"{serviceName} - {prazo} dia(s)";
            if (!string.IsNullOrEmpty(obs))
                shippingName += $" - {obs}";
            return new ShippingOption() { Rate = _correiosService.GetConvertedRateToPrimaryCurrency(rate), Name = shippingName };
        }

        private int CalcPrazoEntrega(WSCorreiosCalcPrecoPrazo.cServico serv)
        {
            int prazo = Convert.ToInt32(serv.PrazoEntrega);
            if (_correiosSettings.AddDaysForDelivery > 0)
                prazo += _correiosSettings.AddDaysForDelivery;
            return prazo;
        }

        private string ValidateWSResult(WSCorreiosCalcPrecoPrazo.cServico wsServico)
        {
            string retorno = string.Empty;
            if (!string.IsNullOrEmpty(wsServico.Erro) && (wsServico.Erro != "0"))
            {
                if ((wsServico.Erro == "009") || (wsServico.Erro == "010") || (wsServico.Erro == "011"))
                    retorno = wsServico.MsgErro;
                else
                    throw new NopException(wsServico.Erro + " - " + wsServico.MsgErro);
            }

            if (Convert.ToInt32(wsServico.PrazoEntrega) <= 0)
                throw new NopException(_localizationService.GetResource("Plugins.Shipping.Correios.Message.DeliveryUninformed"));

            if (Convert.ToDecimal(wsServico.Valor, new CultureInfo("pt-BR")) <= 0)
                throw new NopException(_localizationService.GetResource("Plugins.Shipping.Correios.Message.InvalidValueDelivery"));

            return retorno;
        }

        public decimal? GetFixedRate(GetShippingOptionRequest getShippingOptionRequest) => null;

        public override string GetConfigurationPageUrl() => _webHelper.GetStoreLocation() + "Admin/ShippingCorreios/Configure";

        public override void Install()
        {
            var settings = new CorreiosSettings()
            {
                Url = "http://ws.correios.com.br/calculador/CalcPrecoPrazo.asmx",
                PostalCodeFrom = "",
                CompanyCode = "",
                Password = "",
                AddDaysForDelivery = 0,
                PercentageShippingFee = 1.0M
            };
            _settingService.SaveSetting(settings);

            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.Url", "URL");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.Url.Hint", "Specify Correios URL.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.PostalCodeFrom", "Postal Code From");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.PostalCodeFrom.Hint", "Specify From Postal Code.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.CompanyCode", "Company Code");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.CompanyCode.Hint", "Specify Your Company Code.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.Password", "Password");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.Password.Hint", "Specify Your Password.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.AddDaysForDelivery", "Additional Days For Delivery");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.AddDaysForDelivery.Hint", "Set The Amount Of Additional Days For Delivery.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.AvailableCarrierServices", "Available Carrier Services");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.AvailableCarrierServices.Hint", "Set Available Carrier Services.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.ServiceNameDefault", "Service Name Default");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.ServiceNameDefault.Hint", "Service Name Used When The Correios Does Not Return Value.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.ShippingRateDefault", "Shipping Rate Default");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.ShippingRateDefault.Hint", "Shipping Rate Used When The Correios Does Not Return Value.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.QtdDaysForDeliveryDefault", "Number Of Days For Delivery Default");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.QtdDaysForDeliveryDefault.Hint", "Number Of Days For Delivery Used When The Correios Does Not Return Value.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.PercentageShippingFee", "Additional percentage shipping fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Fields.PercentageShippingFee.Hint", "Set the additional percentage shipping rate.");

            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.NoShipmentItems", "No shipment items");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.AddressNotSet", "Shipping address is not set");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.CountryNotSet", "Shipping country is not set");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.StateNotSet", "Shipping state is not set");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.PostalCodeNotSet", "Shipping zip postal code is not set");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.DeliveryUninformed", "Delivery uninformed");
            this.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Correios.Message.InvalidValueDelivery", "Invalid value delivery");

            base.Install();
        }

        public override void Uninstall()
        {
            _settingService.DeleteSetting<CorreiosSettings>();

            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.Url");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.Url.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.PostalCodeFrom");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.PostalCodeFrom.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.CompanyCode");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.CompanyCode.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.Password");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.Password.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.AddDaysForDelivery");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.AddDaysForDelivery.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.AvailableCarrierServices");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.AvailableCarrierServices.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.ServiceNameDefault");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.ServiceNameDefault.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.ShippingRateDefault");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.ShippingRateDefault.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.QtdDaysForDeliveryDefault");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.QtdDaysForDeliveryDefault.Hint");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.PercentageShippingFee");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Fields.PercentageShippingFee.Hint");

            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.NoShipmentItems");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.AddressNotSet");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.CountryNotSet");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.StateNotSet");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.PostalCodeNotSet");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.DeliveryUninformed");
            this.DeletePluginLocaleResource("Plugins.Shipping.Correios.Message.InvalidValueDelivery");

            base.Uninstall();
        }

        public ShippingRateComputationMethodType ShippingRateComputationMethodType => ShippingRateComputationMethodType.Realtime;

        public IShipmentTracker ShipmentTracker => new CorreiosShipmentTracker(_correiosSettings);
    }
}